/// <reference types="vitest/config" />
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [react()],

  server: {
    // Matches FRONTEND_PORT in .env.example and the Compose port mapping.
    port: 3100,
    // Required so the dev server is reachable from outside its container.
    host: true,
    strictPort: true,
  },

  preview: {
    port: 3100,
    host: true,
    strictPort: true,
  },

  build: {
    outDir: "dist",
    sourcemap: true,
  },

  test: {
    environment: "jsdom",
    globals: true,

    // Vitest defaults to the 'forks' pool, whose workers fail to hand-shake on
    // this toolchain (Node 26 on Windows) and time out before any test runs.
    // Worker threads start reliably and are faster here.
    pool: "threads",

    setupFiles: ["./vitest.setup.ts"],
    include: ["src/**/*.test.{ts,tsx}"],
    coverage: {
      provider: "v8",
      reporter: ["text", "lcov"],
      include: ["src/**/*.{ts,tsx}"],
      exclude: ["src/**/*.test.{ts,tsx}", "src/main.tsx"],
    },
  },
});
