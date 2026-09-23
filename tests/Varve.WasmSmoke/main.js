// The host page's entry point. wasm-experimental, not Blazor: the claim under
// test is that Varve runs in a browser, and a web framework would add a package
// tail that has nothing to do with it.
import { dotnet } from './_framework/dotnet.js';

const { getAssemblyExports, getConfig } = await dotnet.create();
const exports = await getAssemblyExports(getConfig().mainAssemblyName);
const report = await exports.Varve.WasmSmoke.Smoke.Run();

document.getElementById('out').textContent = report;
globalThis.varveSmokeReport = report;
