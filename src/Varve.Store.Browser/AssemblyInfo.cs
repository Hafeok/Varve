// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Every type here calls into a browser through JavaScript interop, which the
// platform-compatibility analyzer knows is browser-only. The assembly says so
// once, rather than every call site (ADR 0084).

using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("browser")]
