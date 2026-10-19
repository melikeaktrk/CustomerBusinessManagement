using CustomerBusinessManagement.Business;
using CustomerBusinessManagement.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerBusinessManagement.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.Admin)]
[ApiController]
[Route("api/employees/{employeeId:int}/leave")]
public sealed class EmployeeLeavesController(ILeaveService leaveService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetStatus(int employeeId, CancellationToken ct)
    {
        try
        {
            return Ok(await leaveService.GetStatusAsync(employeeId, ct: ct));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create(int employeeId, LeaveRequest request, CancellationToken ct)
    {
        if (request.EmployeeId != employeeId)
            return BadRequest(new { message = "Personel kimliği izin isteğiyle eşleşmiyor." });
        try
        {
            return Ok(await leaveService.CreateAsync(request, ct));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
}

[Authorize(Policy = AuthorizationPolicies.Admin)]
[ApiController]
[Route("api/employee-leaves")]
public sealed class EmployeeLeaveRecordsController(ILeaveService leaveService) : ControllerBase
{
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        try
        {
            await leaveService.DeleteAsync(id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}