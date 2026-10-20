// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Passes: upstream, allowed for this file, pinned by digest, and overridden
// to the mirror by the job its allowance names.
builder.AddContainer("allowed", configured ?? "ghcr.io/example/allowed", "3.0").WithImageSHA256("cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc");
