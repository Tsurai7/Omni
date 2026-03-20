import { defineConfig } from "astro/config";

// https://astro.build/config
export default defineConfig({
  output: "static",
  site: "https://tsurai7.github.io",
  base: "/Omni",
  server: { host: "127.0.0.1" },
  vite: { server: { host: "127.0.0.1" } },
});
