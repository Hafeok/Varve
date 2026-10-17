// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Store.Log;

/// <summary>An object as a list returns it: its name, its length and its version.</summary>
/// <param name="Name">The object's name.</param>
/// <param name="Length">How many bytes it holds.</param>
/// <param name="Version">Its current version.</param>
public readonly record struct ObjectEntry(ObjectName Name, ByteCount Length, ObjectVersion Version);
