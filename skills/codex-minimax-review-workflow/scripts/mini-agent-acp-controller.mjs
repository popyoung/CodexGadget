#!/usr/bin/env node
import { spawn } from "node:child_process";
import { createHash, randomUUID } from "node:crypto";
import { existsSync } from "node:fs";
import { mkdir, readFile, stat, writeFile } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";

const DEFAULT_COMMAND = process.env.MINI_AGENT_ACP
  || "C:\\Users\\popyoung\\.local\\bin\\mini-agent-acp.exe";

function parseArgs(argv) {
  const result = {
    workspace: undefined,
    command: DEFAULT_COMMAND,
    heartbeatMs: 120_000,
    stageReportMs: 300_000,
    gitProxy: process.env.MINIMAX_GIT_PROXY,
    pollMs: 2_000,
    promptTimeoutMs: 1_800_000,
    exitAfterTurns: undefined
  };

  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    const value = argv[index + 1];
    if (arg === "--workspace") {
      result.workspace = value;
    } else if (arg === "--command") {
      result.command = value;
    } else if (arg === "--heartbeat-ms") {
      result.heartbeatMs = Number(value);
    } else if (arg === "--stage-report-ms") {
      result.stageReportMs = Number(value);
    } else if (arg === "--git-proxy") {
      result.gitProxy = value;
    } else if (arg === "--poll-ms") {
      result.pollMs = Number(value);
    } else if (arg === "--prompt-timeout-ms") {
      result.promptTimeoutMs = Number(value);
    } else if (arg === "--exit-after-turns") {
      result.exitAfterTurns = Number(value);
    } else {
      throw new Error(`Unknown argument: ${arg}`);
    }
    index += 1;
  }

  if (!result.workspace) {
    throw new Error("--workspace is required");
  }
  for (const [name, value] of Object.entries({
    heartbeatMs: result.heartbeatMs,
    stageReportMs: result.stageReportMs,
    pollMs: result.pollMs,
    promptTimeoutMs: result.promptTimeoutMs
  })) {
    if (!Number.isFinite(value) || value <= 0) {
      throw new Error(`--${name.replace(/[A-Z]/g, c => `-${c.toLowerCase()}`)} must be a positive number`);
    }
  }
  if (result.exitAfterTurns !== undefined && (!Number.isFinite(result.exitAfterTurns) || result.exitAfterTurns < 1)) {
    throw new Error("--exit-after-turns must be a positive number");
  }
  return result;
}

function nowIso() {
  return new Date().toISOString();
}

function sha256(text) {
  return createHash("sha256").update(text).digest("hex");
}

function sleep(ms) {
  return new Promise(resolveSleep => setTimeout(resolveSleep, ms));
}

function applyGitProxyEnv(env, gitProxy) {
  if (!gitProxy) {
    return env;
  }

  const baseCount = Number.parseInt(env.GIT_CONFIG_COUNT || "0", 10);
  const nextIndex = Number.isFinite(baseCount) && baseCount >= 0 ? baseCount : 0;
  Object.assign(env, {
    HTTP_PROXY: env.HTTP_PROXY || gitProxy,
    HTTPS_PROXY: env.HTTPS_PROXY || gitProxy,
    ALL_PROXY: env.ALL_PROXY || gitProxy,
    http_proxy: env.http_proxy || gitProxy,
    https_proxy: env.https_proxy || gitProxy,
    all_proxy: env.all_proxy || gitProxy,
    GIT_CONFIG_COUNT: String(nextIndex + 2),
    [`GIT_CONFIG_KEY_${nextIndex}`]: "http.proxy",
    [`GIT_CONFIG_VALUE_${nextIndex}`]: gitProxy,
    [`GIT_CONFIG_KEY_${nextIndex + 1}`]: "https.proxy",
    [`GIT_CONFIG_VALUE_${nextIndex + 1}`]: gitProxy
  });
  return env;
}

class AcpConnection {
  constructor({ command, cwd, gitProxy, logPath, promptTimeoutMs }) {
    this.command = command;
    this.cwd = cwd;
    this.gitProxy = gitProxy;
    this.logPath = logPath;
    this.promptTimeoutMs = promptTimeoutMs;
    this.nextId = 1;
    this.buffer = "";
    this.pending = new Map();
    this.closed = false;
    this.lastOutputAt = undefined;
  }

  async start() {
    const env = { ...process.env, PYTHONIOENCODING: "utf-8" };
    applyGitProxyEnv(env, this.gitProxy);

    this.child = spawn(this.command, [], {
      cwd: this.cwd,
      env,
      stdio: ["pipe", "pipe", "pipe"],
      windowsHide: true
    });

    this.child.stdout.on("data", chunk => {
      this.lastOutputAt = nowIso();
      this.buffer += chunk.toString("utf8");
      this.#drainLines().catch(error => {
        this.#appendLog({ type: "controller-error", error: error.message }).catch(() => {});
      });
    });

    this.child.stderr.on("data", chunk => {
      this.lastOutputAt = nowIso();
      this.#appendLog({ type: "stderr", text: chunk.toString("utf8") }).catch(() => {});
    });

    this.child.on("exit", (code, signal) => {
      this.closed = true;
      for (const { reject, timer } of this.pending.values()) {
        clearTimeout(timer);
        reject(new Error(`mini-agent-acp exited: code=${code} signal=${signal}`));
      }
      this.pending.clear();
      this.#appendLog({ type: "exit", code, signal }).catch(() => {});
    });
  }

  async request(method, params) {
    if (this.closed) {
      throw new Error("ACP connection is closed");
    }
    const id = this.nextId;
    this.nextId += 1;
    const payload = { jsonrpc: "2.0", id, method, params };
    await this.#appendLog({ direction: "out", message: payload });
    this.child.stdin.write(`${JSON.stringify(payload)}\n`);
    return new Promise((resolveRequest, rejectRequest) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        rejectRequest(new Error(`ACP request timed out: ${method}`));
      }, this.promptTimeoutMs);
      this.pending.set(id, { resolve: resolveRequest, reject: rejectRequest, timer });
    });
  }

  async close() {
    if (this.closed) {
      return;
    }
    this.closed = true;
    this.child.kill();
  }

  async #drainLines() {
    let newlineIndex;
    while ((newlineIndex = this.buffer.indexOf("\n")) >= 0) {
      const line = this.buffer.slice(0, newlineIndex).trim();
      this.buffer = this.buffer.slice(newlineIndex + 1);
      if (!line) {
        continue;
      }
      let message;
      try {
        message = JSON.parse(line);
      } catch (error) {
        await this.#appendLog({ type: "parse-error", line, error: error.message });
        continue;
      }
      await this.#appendLog({ direction: "in", message });
      if (message.id !== undefined && this.pending.has(message.id)) {
        const pending = this.pending.get(message.id);
        this.pending.delete(message.id);
        clearTimeout(pending.timer);
        if (message.error) {
          pending.reject(new Error(JSON.stringify(message.error)));
        } else {
          pending.resolve(message.result);
        }
      } else if (message.id !== undefined && message.method) {
        const response = {
          jsonrpc: "2.0",
          id: message.id,
          error: {
            code: -32601,
            message: `Unsupported client request: ${message.method}`
          }
        };
        this.child.stdin.write(`${JSON.stringify(response)}\n`);
        await this.#appendLog({ direction: "out", message: response });
      }
    }
  }

  async #appendLog(record) {
    await writeFile(this.logPath, `${JSON.stringify({ at: nowIso(), ...record })}\n`, { flag: "a", encoding: "utf8" });
  }
}

async function writeJson(path, value) {
  await mkdir(dirname(path), { recursive: true });
  await writeFile(path, `${JSON.stringify(value, null, 2)}\n`, "utf8");
}

async function readTurn(inputPath) {
  if (!existsSync(inputPath)) {
    return undefined;
  }
  const info = await stat(inputPath);
  if (!info.isFile() || info.size === 0) {
    return undefined;
  }
  const text = await readFile(inputPath, "utf8");
  if (!text.trim()) {
    return undefined;
  }
  return {
    text,
    hash: sha256(text),
    modifiedMs: info.mtimeMs,
    size: info.size
  };
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const workspace = resolve(args.workspace);
  const remixDir = join(workspace, ".minimax-remix");
  await mkdir(remixDir, { recursive: true });

  const sessionId = `acp-${randomUUID()}`;
  const heartbeatPath = join(remixDir, "session-heartbeat.json");
  const inputPath = join(remixDir, "codex-to-minimax.md");
  const minimaxHeartbeatPath = join(remixDir, "minimax-heartbeat.json");
  const partialOutputPath = join(remixDir, "minimax-to-codex.partial.md");
  const finalOutputPath = join(remixDir, "minimax-to-codex.md");
  const stopPath = join(remixDir, "stop-session");
  const logPath = join(remixDir, "session-log.ndjson");

  let status = "starting";
  let currentTurn = 0;
  let lastTurnHash;
  let lastError;

  const connection = new AcpConnection({
    command: args.command,
    cwd: workspace,
    gitProxy: args.gitProxy,
    logPath,
    promptTimeoutMs: args.promptTimeoutMs
  });

  const heartbeat = async () => {
    await writeJson(heartbeatPath, {
      sessionId,
      pid: connection.child?.pid,
      status,
      currentTurn,
      lastTurnHash,
      lastError,
      heartbeatMs: args.heartbeatMs,
      expectedMiniMaxHeartbeatMs: args.heartbeatMs,
      expectedMiniMaxStageReportMs: args.stageReportMs,
      gitProxy: args.gitProxy,
      lastProcessOutputAt: connection.lastOutputAt,
      lastHeartbeatAt: nowIso(),
      inputPath,
      minimaxHeartbeatPath,
      partialOutputPath,
      outputPath: finalOutputPath,
      logPath
    });
  };

  await connection.start();
  await heartbeat();
  const heartbeatTimer = setInterval(() => {
    heartbeat().catch(() => {});
  }, args.heartbeatMs);

  try {
    status = "initializing";
    await heartbeat();
    await connection.request("initialize", {
      protocolVersion: 1,
      clientCapabilities: {},
      clientInfo: {
        name: "minimax-remix-controller",
        title: "MiniMax Remix Controller",
        version: "0.1.0"
      }
    });
    const session = await connection.request("session/new", {
      cwd: workspace,
      additionalDirectories: [],
      mcpServers: []
    });

    status = "idle";
    await heartbeat();
    await writeFile(logPath, `${JSON.stringify({ at: nowIso(), type: "session", session })}\n`, { flag: "a", encoding: "utf8" });

    while (!existsSync(stopPath)) {
      const turn = await readTurn(inputPath);
      if (!turn || turn.hash === lastTurnHash) {
        await sleep(args.pollMs);
        continue;
      }

      currentTurn += 1;
      lastTurnHash = turn.hash;
      status = "working";
      lastError = undefined;
      await heartbeat();

      try {
        const result = await connection.request("session/prompt", {
          sessionId: session.sessionId,
          prompt: [{ type: "text", text: turn.text }]
        });
        await writeFile(
          logPath,
          `${JSON.stringify({ at: nowIso(), type: "turn-result", currentTurn, result })}\n`,
          { flag: "a", encoding: "utf8" }
        );
        status = "idle";
      } catch (error) {
        status = "error";
        lastError = error.message;
      }
      await heartbeat();

      if (args.exitAfterTurns !== undefined && currentTurn >= args.exitAfterTurns) {
        break;
      }
    }
  } finally {
    clearInterval(heartbeatTimer);
    status = status === "error" ? status : "stopped";
    await heartbeat();
    await connection.close();
  }
}

main().catch(error => {
  console.error(error.stack || error.message);
  process.exitCode = 1;
});
