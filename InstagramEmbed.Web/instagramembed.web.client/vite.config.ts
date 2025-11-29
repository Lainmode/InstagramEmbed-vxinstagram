import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import path from "path";
import tailwindcss from "@tailwindcss/vite";
import generouted from "@generouted/react-router/plugin";

// https://vite.dev/config/
export default defineConfig({
	plugins: [react(), tailwindcss(), generouted()],
	resolve: {
		alias: {
			"@": path.resolve(__dirname, "./src"),
		},
	},
});
