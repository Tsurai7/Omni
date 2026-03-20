export interface Release {
  version: string;
  date: string;
  highlights: string[];
}

/**
 * Add a new entry at the top of the array when you ship a release.
 * The landing will show the latest version badge and a "What's new" popover.
 */
export const changelog: Release[] = [
  {
    version: "v1.0.0",
    date: "2026-03-21",
    highlights: [
      "Initial release",
      "Automatic session tracking",
      "Focus mode with distraction counter",
      "Usage insights dashboard",
      "AI productivity coach",
    ],
  },
];

export const latestRelease = changelog[0];
