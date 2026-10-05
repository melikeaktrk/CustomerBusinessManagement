# CustomerBusinessManagement

Admin-only customer, employee and financial management app. The backend is a .NET 8 layered solution; the frontend is React, TypeScript and Vite.

## Requirements

- .NET 8 SDK
- Node.js 20 or later and npm
- SQL Server, with `CustomerBusinessManagementDb` accessible to the current Windows account

## Run locally

1. Review `backend/CustomerBusinessManagement.API/appsettings.json` and set `ConnectionStrings:DefaultConnection` for the SQL Server instance you use. Do not put production credentials or JWT keys in source control.
2. Store local secrets outside source control with .NET User Secrets:

   ```powershell
   cd backend
   dotnet user-secrets set "Jwt:Key" "<en az 32 karakterlik rastgele anahtar>" --project CustomerBusinessManagement.API
   dotnet user-secrets set "Admin:Email" "<yönetici e-posta adresi>" --project CustomerBusinessManagement.API
   dotnet user-secrets set "Admin:Password" "<güçlü yönetici parolası>" --project CustomerBusinessManagement.API
   ```

   Existing database users keep their current passwords. Admin seed credentials are only used when the database does not already contain that email.
3. Apply EF migrations from `backend`:

   ```powershell
   dotnet ef database update --project CustomerBusinessManagement.DataAccess --startup-project CustomerBusinessManagement.API
   ```

4. Start API: `dotnet run --project CustomerBusinessManagement.API --launch-profile http` (Swagger: `http://localhost:5139/swagger`).
5. In `frontend`, run `npm install` and `npm run dev`. Set `VITE_API_URL` in `.env.local` if the API uses a different URL.

The dev seed is idempotent and runs only in Development. There are no application roles; the context uses Identity's user-only store and the database contains no role tables. All business endpoints and `/api/Auth/me` require a bearer token; `/api/Auth/login` is public.

## OCR

The API runs the real Tesseract CLI; `tur+eng` is the default language pair. Tesseract is not bundled with this application. The current development computer was checked and does not have `tesseract.exe` installed or available on `PATH`.

### Windows setup

1. Install a 64-bit Tesseract 5 Windows build from the UB Mannheim installer linked by the [Tesseract Windows installation guide](https://tesseract-ocr.github.io/tessdoc/Installation.html). Include English data if the installer offers language options.
2. Download `tur.traineddata` from the [official Tesseract language data repository](https://github.com/tesseract-ocr/tessdata_fast/blob/main/tur.traineddata) and place it in the install's `tessdata` folder. `eng.traineddata` must also be there when using `tur+eng`.
3. In the same PowerShell window that will start the API, set paths for your actual installation. Adjust them if you chose another location:

   ```powershell
   $env:Ocr__TesseractPath = "C:\Program Files\Tesseract-OCR\tesseract.exe"
   $env:Ocr__TessdataPath = "C:\Program Files\Tesseract-OCR\tessdata"
   $env:Ocr__Languages = "tur+eng"
   ```

   Environment variables override `appsettings.json` and `appsettings.Development.json`. Alternatively, put the same keys under the `Ocr` section in .NET User Secrets. Leaving `TesseractPath` empty uses `tesseract.exe` from the API process's `PATH`; an arbitrary machine-specific path is not embedded in source.
4. Verify the executable and both models before starting the API:

   ```powershell
   & $env:Ocr__TesseractPath --version
   & $env:Ocr__TesseractPath --tessdata-dir $env:Ocr__TessdataPath --list-langs
   ```

   The language list must include `eng` and `tur`. The API passes `Ocr:TessdataPath` to Tesseract explicitly, so the service process does not depend on a global `TESSDATA_PREFIX` setting.
5. PNG and JPG are processed directly. PDF pages are rendered using Poppler's `pdftoppm`; install Poppler for Windows and set `$env:Ocr__PdfToPpmPath` to the actual `pdftoppm.exe` path. The API returns an understandable 503 when an OCR executable or language model is missing. A document the installed engine cannot decode is stored and returns a manual-entry path instead of invented amounts.

The OCR parser returns null for unrecognized amounts, tolerates Turkish/English labels and common Turkish/decimal currency separators, and derives net/total only when their source amounts are both recognized. The admin must review and confirm every Z report. Files are signature-checked and stored under `App_Data/ZReports` inside the API content root, then served only through the authenticated report-document endpoint.

## Validate

```powershell
cd backend
dotnet restore CustomerBusinessManagement.sln
dotnet build CustomerBusinessManagement.sln
dotnet test CustomerBusinessManagement.sln
cd ..\frontend
npm install
npm run build
```

