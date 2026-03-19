function publicEnv(key: string): string | undefined {
  const v = import.meta.env[key];
  return typeof v === "string" && v.length > 0 ? v : undefined;
}

/** Build-time URLs; set PUBLIC_DOWNLOAD_MAC_URL and PUBLIC_DOWNLOAD_WIN_URL in CI or .env */
export const downloads = {
  macUrl:
    publicEnv("PUBLIC_DOWNLOAD_MAC_URL") ?? "https://example.com/omni-macos",
  windowsUrl:
    publicEnv("PUBLIC_DOWNLOAD_WIN_URL") ?? "https://example.com/omni-windows",
  versionLabel: publicEnv("PUBLIC_APP_VERSION") ?? "",
} as const;
