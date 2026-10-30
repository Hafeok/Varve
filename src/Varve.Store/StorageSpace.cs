// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;

namespace Varve.Store;

/// <summary>
/// Whether a failure is the storage having no space for what was written: a
/// full disk or an exhausted quota (ADR 0214 point 2). Such a write is
/// refused as <c>storage-full</c>, and the next write tries again once the
/// space is back; any other failure of the log is not.
/// </summary>
internal static class StorageSpace
{
    // Windows: ERROR_HANDLE_DISK_FULL and ERROR_DISK_FULL, as HRESULTs.
    private const int HandleDiskFull = unchecked((int)0x80070027);
    private const int DiskFull = unchecked((int)0x80070070);

    // Unix: .NET raises an IOException whose HResult is the raw errno.
    // ENOSPC is 28 on Linux and macOS; EDQUOT is 122 on Linux and 69 on macOS.
    private const int NoSpace = 28;
    private const int QuotaLinux = 122;
    private const int QuotaMac = 69;

    /// <summary>Whether <paramref name="error"/>, or an exception inside it, is the storage being full.</summary>
    internal static bool IsFull(Exception? error)
    {
        for (Exception? e = error; e is not null; e = e.InnerException)
        {
            if (e is IOException { HResult: HandleDiskFull or DiskFull or NoSpace or QuotaLinux or QuotaMac })
            {
                return true;
            }
        }

        return false;
    }
}
