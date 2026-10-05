// A dedicated worker: .NET runs here, with synchronous access handles.
import { run } from './run.js';

postMessage(await run('worker'));
