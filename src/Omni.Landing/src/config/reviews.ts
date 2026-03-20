export interface Review {
  name: string;
  role: string;
  quote: string;
  stars: number;
  initials: string;
  /** Tailwind-style hex for avatar background */
  avatarColor: string;
  /** Optional path to avatar image in /public/avatars/ */
  avatar?: string;
}

export const reviews: Review[] = [
  {
    name: "Alex K.",
    role: "Software Engineer",
    quote:
      "Finally an app that shows me where my focus actually went. The session tracking is scarily accurate.",
    stars: 5,
    initials: "AK",
    avatarColor: "#7c3aed",
    // avatar: "/avatars/alex.jpg",
  },
  {
    name: "Maria S.",
    role: "Product Designer",
    quote:
      "I've tried every productivity app out there. Omni is the first one I kept using after the first week.",
    stars: 5,
    initials: "MS",
    avatarColor: "#0ea5e9",
    // avatar: "/avatars/maria.jpg",
  },
  {
    name: "Tom R.",
    role: "Indie Hacker",
    quote:
      "The AI coach actually reads my data and gives real advice. Not just 'try the Pomodoro technique'.",
    stars: 5,
    initials: "TR",
    avatarColor: "#10b981",
    // avatar: "/avatars/tom.jpg",
  },
];
