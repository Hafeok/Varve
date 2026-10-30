// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Store.Log;

/// <summary>
/// What <see cref="Dataset.TryReadCheckedAsync"/> read: the bytes a
/// projection wrote, every block of them verified, and the position they
/// were built at, which the dataset's log holds (ADR 0213).
/// </summary>
/// <param name="Position">The position the projection was built at.</param>
/// <param name="Bytes">The projection's bytes.</param>
public sealed record CheckedContent(Position Position, ReadOnlyMemory<byte> Bytes);
