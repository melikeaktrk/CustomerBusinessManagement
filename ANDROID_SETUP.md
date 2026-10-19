# Android Setup

This project packages the existing React/Vite frontend with Capacitor 8. The ASP.NET Core backend and SQL Server remain separate and unchanged.

## Requirements

- Node.js 22 or later and npm. Verified locally: Node.js 24.14.1, npm 11.11.0.
- Android Studio with Android SDK Platform 37, Android SDK Build-Tools 36, Android Emulator, and an API 24+ emulator image.
- A supported JDK selected by Android Studio/Gradle. The installed Android Studio includes its own JBR; the system Java 8 is too old for current Android builds. Verify the Gradle JDK in Android Studio settings before building.
- The API running on the Windows host at `http://localhost:5139` for emulator testing.

The Android SDK and Android Studio were detected on this computer. No emulator was running when this setup was inspected.

## Install dependencies

From `frontend`:

```powershell
npm install
```

Capacitor `8.5.3` is installed with the Android platform and CLI. The Android emulator URL is in the ignored, machine-local `frontend/.env.android.local`; the existing web `.env.local` remains `http://localhost:5139/api`.

## Build the web assets

```powershell
cd frontend
npm run build
```

The Vite output directory is `frontend/dist`, and `capacitor.config.ts` uses `webDir: "dist"`.

For an Android emulator build, Vite uses mode `android`:

```powershell
npm run build:android
```

The emulator can reach a service on the host computer at `10.0.2.2`; the local Android mode points to `http://10.0.2.2:5139/api`. Keep the API running on the host while testing. The Capacitor HTTP bridge is enabled so Axios/XHR uses native HTTP and does not depend on changing backend CORS policy.

## Add and synchronize Android

Add the platform once:

```powershell
npm run cap:add:android
```

Build and sync web assets to Android:

```powershell
npm run build:android
$env:CAPACITOR_ANDROID_CLEARTEXT = "true"
npm run cap:sync:android
Remove-Item Env:CAPACITOR_ANDROID_CLEARTEXT
```

Cleartext HTTP is enabled only for this emulator-development sync. Do not use that setting for a release APK. For a real device or release build, set `VITE_API_URL` to the deployed HTTPS API endpoint and leave `CAPACITOR_ANDROID_CLEARTEXT` unset.

## Open Android Studio and run

```powershell
npm run cap:open:android
```

In Android Studio, select or create an emulator (API 24 or later), wait for Gradle sync, then press **Run**. The emulator and host API must be on the same development setup; `localhost` inside the emulator refers to the emulator, so the app uses `10.0.2.2` for the Windows host.

To run from the command line after an emulator has started:

```powershell
npm run cap:run:android
```

## Create an APK

For a local debug APK, complete the emulator-development sync above, then run:

```powershell
cd android
.
gradlew.bat assembleDebug
```

The expected output is `frontend/android/app/build/outputs/apk/debug/app-debug.apk`. Confirm the file exists before treating the APK build as successful. A distributable release requires a deployed HTTPS API and a properly configured signing key; do not commit signing credentials.

The current application uses JWT login and stores the token in session storage. Capacitor packages the same React routes and API client, so role policies remain enforced by the backend. No backend, SQL Server, migration, JWT, or role changes are part of the Android wrapper.