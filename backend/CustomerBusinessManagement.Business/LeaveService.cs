using CustomerBusinessManagement.DataAccess;
using CustomerBusinessManagement.DTO;
using CustomerBusinessManagement.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustomerBusinessManagement.Business;

public interface ILeaveService
{
    Task<LeaveStatusResponse> GetStatusAsync(int employeeId, DateTime? asOf = null, CancellationToken ct = default);
    Task<LeaveRecordResponse> CreateAsync(LeaveRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class LeaveService(ApplicationDbContext db, TimeProvider? clock = null) : ILeaveService
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<LeaveStatusResponse> GetStatusAsync(int employeeId, DateTime? asOf = null, CancellationToken ct = default)
    {
        var now = (asOf ?? _clock.GetUtcNow().DateTime).Date;
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(x => x.Id == employeeId, ct)
            ?? throw new KeyNotFoundException("Personel bulunamadı.");
        var records = await db.EmployeeLeaveRecords
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.LeaveStartDate)
            .ToListAsync(ct);
        var cycleStart = GetCycleStartDate(employee.HireDate, DateOnly.FromDateTime(now));
        var cycleEnd = cycleStart.AddYears(1);
        var currentYearRecords = records
            .Where(x => x.LeaveStartDate >= cycleStart && x.LeaveEndDate < cycleEnd)
            .ToList();
        var balance = GetBalance(
            employee.HireDate,
            cycleStart,
            currentYearRecords
        );
        var history = records.Select(x => new LeaveRecordResponse(
            x.Id,
            x.EmployeeId,
            x.LeaveStartDate,
            x.LeaveEndDate,
            CountWeekdays(x.LeaveStartDate, x.LeaveEndDate),
            x.LeaveType,
            x.Description,
            x.CreatedAt
        )).ToList();
        return new LeaveStatusResponse(
            employeeId,
            balance.EntitlementDays,
            balance.UsedDays,
            balance.RemainingDays,
            history,
            records
                .SelectMany(record => CountDaysByMonth(record.LeaveStartDate, record.LeaveEndDate))
                .GroupBy(item => new { item.Year, item.Month })
                .Select(group => new MonthlyLeaveUsageResponse(group.Key.Year, group.Key.Month, group.Sum(item => item.Days)))
                .OrderByDescending(item => item.Year)
                .ThenByDescending(item => item.Month)
                .ToList()
        );
    }

    public async Task<LeaveRecordResponse> CreateAsync(LeaveRequest request, CancellationToken ct = default)
    {
        if (request.LeaveStartDate > request.LeaveEndDate)
            throw new ArgumentException("İzin başlangıç tarihi bitiş tarihinden sonra olmamalı.");
        if (!string.Equals(request.LeaveType, "Yıllık İzin", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Sadece yıllık izin destekleniyor.");
        if (string.IsNullOrWhiteSpace(request.Description))
            throw new ArgumentException("İzin açıklaması gereklidir.");

        var employee = await db.Employees.SingleOrDefaultAsync(x => x.Id == request.EmployeeId, ct)
            ?? throw new KeyNotFoundException("Personel bulunamadı.");
        var cycleStart = GetCycleStartDate(employee.HireDate, request.LeaveStartDate);
        var cycleEnd = cycleStart.AddYears(1);
        if (request.LeaveStartDate < cycleStart || request.LeaveEndDate >= cycleEnd)
            throw new ArgumentException("İzin tarihleri aynı çalışma yılı dönemi içinde olmalıdır.");
        var numberOfDays = CountWeekdays(request.LeaveStartDate, request.LeaveEndDate);
        if (numberOfDays == 0)
            throw new ArgumentException("İzin aralığında en az bir iş günü bulunmalıdır.");
        var records = await db.EmployeeLeaveRecords
            .Where(x => x.EmployeeId == request.EmployeeId && x.LeaveStartDate >= cycleStart && x.LeaveEndDate < cycleEnd)
            .ToListAsync(ct);
        if (records.Any(x => x.LeaveStartDate <= request.LeaveEndDate && x.LeaveEndDate >= request.LeaveStartDate))
            throw new InvalidOperationException("Bu tarihlerde kayıtlı başka bir izin bulunuyor.");
        var balance = GetBalance(
            employee.HireDate,
            cycleStart,
            records
        );
        if (balance.EntitlementDays == 0)
            throw new InvalidOperationException("Yıllık izin hakkı için personelin en az bir çalışma yılını doldurması gerekir.");
        if (numberOfDays > balance.RemainingDays)
            throw new InvalidOperationException("Kullanılabilir yıllık izin miktarından daha fazla izin kullanılamaz.");

        var record = new EmployeeLeaveRecord
        {
            EmployeeId = employee.Id,
            LeaveStartDate = request.LeaveStartDate,
            LeaveEndDate = request.LeaveEndDate,
            NumberOfDays = numberOfDays,
            LeaveType = request.LeaveType.Trim(),
            Description = request.Description.Trim(),
            LeaveYear = cycleStart.Year,
        };
        db.EmployeeLeaveRecords.Add(record);
        await db.SaveChangesAsync(ct);
        await RecalculateBalanceAsync(employee, db, cycleStart, ct);
        return new LeaveRecordResponse(
            record.Id,
            record.EmployeeId,
            record.LeaveStartDate,
            record.LeaveEndDate,
            record.NumberOfDays,
            record.LeaveType,
            record.Description,
            record.CreatedAt
        );
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var record = await db.EmployeeLeaveRecords.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("İzin kaydı bulunamadı.");
        var employee = await db.Employees.SingleOrDefaultAsync(x => x.Id == record.EmployeeId, ct)
            ?? throw new KeyNotFoundException("Personel bulunamadı.");
        db.EmployeeLeaveRecords.Remove(record);
        await db.SaveChangesAsync(ct);
        await RecalculateBalanceAsync(
            employee,
            db,
            GetCycleStartDate(employee.HireDate, record.LeaveStartDate),
            ct
        );
    }

    private static LeaveBalance GetBalance(
        DateOnly registrationDate,
        DateOnly asOf,
        IReadOnlyCollection<EmployeeLeaveRecord> records)
    {
        var entitlement = asOf > registrationDate.AddYears(15)
            ? 26
            : asOf >= registrationDate.AddYears(5)
                ? 20
                : asOf >= registrationDate.AddYears(1)
                    ? 14
                    : 0;
        var used = records.Sum(x => CountWeekdays(x.LeaveStartDate, x.LeaveEndDate));
        return new LeaveBalance(entitlement, used, Math.Max(0, entitlement - used));
    }

    private async Task RecalculateBalanceAsync(
        Employee employee,
        ApplicationDbContext db,
        DateOnly cycleStart,
        CancellationToken ct)
    {
        var cycleEnd = cycleStart.AddYears(1);
        var records = await db.EmployeeLeaveRecords
            .Where(x => x.EmployeeId == employee.Id && x.LeaveStartDate >= cycleStart && x.LeaveEndDate < cycleEnd)
            .ToListAsync(ct);
        var balance = GetBalance(
            employee.HireDate,
            cycleStart,
            records
        );
        var existing = await db.EmployeeLeaveBalances
            .SingleOrDefaultAsync(x => x.EmployeeId == employee.Id && x.LeaveYear == cycleStart.Year, ct);
        if (existing is null)
        {
            existing = new EmployeeLeaveBalance
            {
                EmployeeId = employee.Id,
                LeaveYear = cycleStart.Year,
                EntitlementDays = balance.EntitlementDays,
                UsedDays = balance.UsedDays,
                RemainingDays = balance.RemainingDays,
                CalculatedAt = _clock.GetUtcNow().UtcDateTime,
            };
            db.EmployeeLeaveBalances.Add(existing);
        }
        else
        {
            existing.EntitlementDays = balance.EntitlementDays;
            existing.UsedDays = balance.UsedDays;
            existing.RemainingDays = balance.RemainingDays;
            existing.CalculatedAt = _clock.GetUtcNow().UtcDateTime;
        }
        await db.SaveChangesAsync(ct);
    }

    private static int GetCompletedYears(DateOnly registrationDate, DateOnly now)
    {
        var years = now.Year - registrationDate.Year;
        return years > 0 && now < registrationDate.AddYears(years) ? years - 1 : years;
    }

    private static DateOnly GetCycleStartDate(DateOnly hireDate, DateOnly asOf)
    {
        var completedYears = Math.Max(0, GetCompletedYears(hireDate, asOf));
        return hireDate.AddYears(completedYears);
    }

    private static int CountWeekdays(DateOnly start, DateOnly end)
    {
        var count = 0;
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            count++;
        }
        return count;
    }

    private static IEnumerable<MonthlyLeaveUsageResponse> CountDaysByMonth(DateOnly start, DateOnly end)
    {
        var counts = new Dictionary<(int Year, int Month), int>();
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            var key = (date.Year, date.Month);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        return counts.Select(item => new MonthlyLeaveUsageResponse(item.Key.Year, item.Key.Month, item.Value));
    }

    private sealed record LeaveBalance(int EntitlementDays, int UsedDays, int RemainingDays);
}
