import { spawn } from 'node:child_process';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const frontendDir = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const backendProject = '../backend/CashBoard.Api/CashBoard.Api.csproj';
const isWindows = process.platform === 'win32';
const dotnet = isWindows ? 'C:\\Progra~1\\dotnet\\dotnet.exe' : 'dotnet';
const npm = isWindows ? 'npm.cmd' : 'npm';
const children = [];

function run(command, args, label) {
  const executable = isWindows ? (process.env.ComSpec || 'C:\\Windows\\System32\\cmd.exe') : command;
  const commandArgs = isWindows
    ? ['/d', '/c', [command, ...args].map(value => value.includes(' ') ? `"${value}"` : value).join(' ')]
    : args;
  const child = spawn(executable, commandArgs, { cwd: frontendDir, stdio: 'inherit', shell: false });
  children.push(child);
  child.on('error', error => {
    console.error(`\n[${label}] No se pudo iniciar: ${error.message}`);
    shutdown(1);
  });
  child.on('exit', code => {
    if (!stopping && code !== 0) {
      console.error(`\n[${label}] terminó con código ${code}.`);
      shutdown(code ?? 1);
    }
  });
  return child;
}

let stopping = false;
function shutdown(code = 0) {
  if (stopping) return;
  stopping = true;
  for (const child of children) {
    if (!child.killed) child.kill(isWindows ? undefined : 'SIGTERM');
  }
  setTimeout(() => process.exit(code), 250);
}

console.log('\nCashBoard Family: iniciando API y frontend...\n');
run(dotnet, ['run', '--project', backendProject], 'API');
run(npm, ['run', 'start:frontend'], 'Angular');

process.on('SIGINT', () => shutdown(0));
process.on('SIGTERM', () => shutdown(0));
