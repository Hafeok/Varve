// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Store.Log;

/// <summary>Bytes read from an object, with the version they are from and the object's whole length.</summary>
/// <param name="Bytes">The bytes read; the caller's to hold.</param>
/// <param name="Version">The version of the object they were read from.</param>
/// <param name="Length">The object's whole length.</param>
public sealed record ObjectRead(ReadOnlyMemory<byte> Bytes, ObjectVersion Version, ByteCount Length);
