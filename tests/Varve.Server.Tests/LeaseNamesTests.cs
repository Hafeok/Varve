// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using Varve.Server.Commands;
using Varve.Store.ObjectStorage.Model;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// <c>varve lease</c> shows each trigger and release reason by the name the
/// writer record gives it (ADRs 0136, 0179): the same names
/// <c>WriterRecordFormatTests</c> holds the record to, so the epoch history
/// an operator reads is the one the record holds.
/// </summary>
public sealed class LeaseNamesTests
{
    [Fact]
    public void lease_shows_every_trigger_and_reason_by_the_records_name()
    {
        Assert.Equal(["restart", "operator", "abdication", "probe"], Enum.GetValues<ClaimTrigger>().Select(ObjectLease.Shown));
        Assert.Equal(["shutdown", "operator-takeover", "placement", "storage-error", "local-disk-error"], Enum.GetValues<ReleaseReason>().Select(ObjectLease.Shown));
    }
}
