import express from "express";
import { snapsave } from "snapsave-media-downloader";

const app = express();
const port = process.env.SNAPSAVE_PORT || 3200;

app.get("/", (_req, res) => res.json({ status: "ok" }));

app.get("/igdl", async (req, res) => {
  try {
    const { url } = req.query;
    if (!url) return res.status(400).json({ error: "url parameter missing" });

    // snapsave() returns { success, data: { media: [...] } } directly
    const result = await snapsave(url);
    res.json(result);
  } catch (err) {
    console.error("[snapsave] error:", err.message);
    res.status(500).json({ error: "Internal Server Error" });
  }
});

const server = app.listen(port, () => {
  console.log(`[snapsave] ready on port ${port}`);
});

function shutdown() {
  server.close(() => {
    console.log("[snapsave] shutdown complete");
    process.exit(0);
  });
  setTimeout(() => process.exit(1), 5000);
}

process.on("SIGTERM", shutdown);
process.on("SIGINT", shutdown);