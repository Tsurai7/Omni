function publicEnv(key: string): string | undefined {
  const v = import.meta.env[key];
  return typeof v === "string" && v.length > 0 ? v : undefined;
}

/** Build-time URLs; set PUBLIC_DOWNLOAD_MAC_URL and PUBLIC_DOWNLOAD_WIN_URL in CI or .env */
export const downloads = {
  macUrl:
    publicEnv("PUBLIC_DOWNLOAD_MAC_URL") ??
    "https://github.com/Tsurai7/Omni/releases/latest/download/Omni-macos.dmg",
  windowsUrl:
    publicEnv("PUBLIC_DOWNLOAD_WIN_URL") ??
    "https://github.com/Tsurai7/Omni/releases/latest/download/Omni-windows.exe",
  versionLabel: publicEnv("PUBLIC_APP_VERSION") ?? "",
} as const;
