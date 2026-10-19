import type { CapacitorConfig } from "@capacitor/cli";

const config: CapacitorConfig = {
  appId: "com.customerbusinessmanagement.app",
  appName: "Customer Business Management",
  webDir: "dist",
  server: {
    cleartext: process.env.CAPACITOR_ANDROID_CLEARTEXT === "true",
  },
  plugins: {
    CapacitorHttp: {
      enabled: true,
    },
  },
};

export default config;