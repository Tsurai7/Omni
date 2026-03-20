export interface Feature {
  icon: string;
  label: string;
  title: string;
  description: string;
  /** Path relative to /public, e.g. "/screenshots/sessions.png". Leave undefined to show placeholder. */
  screenshot?: string;
}

export const features: Feature[] = [
  {
    icon: "⏱",
    label: "Session Tracking",
    title: "See where your time actually goes",
    description:
      "Automatic session tracking captures your real work patterns without any manual input. Know exactly what you worked on and for how long.",
    // screenshot: "/screenshots/sessions.png",
  },
  {
    icon: "🎯",
    label: "Focus Mode",
    title: "Deep work, without the noise",
    description:
      "Block distractions and drop into focus mode in one click. A built-in timer, clear goal, and distraction counter keep you on track.",
    // screenshot: "/screenshots/focus.png",
  },
  {
    icon: "📊",
    label: "Usage Insights",
    title: "Spot your patterns before they cost you",
    description:
      "Weekly and monthly breakdowns show which apps steal your attention and when you're at your productive peak.",
    // screenshot: "/screenshots/insights.png",
  },
  {
    icon: "🤖",
    label: "AI Coach",
    title: "Advice that knows your actual week",
    description:
      "Personalized recommendations based on your real usage data — not generic tips. The more you use Omni, the smarter it gets.",
    // screenshot: "/screenshots/ai-coach.png",
  },
];
