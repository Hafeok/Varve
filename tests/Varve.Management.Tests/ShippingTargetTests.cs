// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using Varve.Management.Model;
using Xunit;

namespace Varve.Management.Tests;

/// <summary>
/// The shipping target (ADR 0184 decision 1): how <c>varve:shipsTo</c> and
/// <c>varve:ShippingTarget</c> read, and what the residual check refuses.
/// </summary>
public sealed class ShippingTargetTests
{
    private const string Orders = """
        <https://w3id.org/varve/ns#manage/dataset/orders> {
          n:orders a varve:Dataset ; varve:datasetName "orders" ; varve:storage varve:ObjectStorage ;
            varve:shipsTo n:replica , n:archive .
          n:replica a varve:ShippingTarget ;
            varve:store "https://acct2.blob.core.windows.net/replicas" ;
            varve:prefix "orders" ;
            varve:provider "azure-blob" ;
            varve:shippedOn "varve-2" .
          n:archive a varve:ShippingTarget ; varve:directory "/var/lib/varve/archive/orders" .
        }
        """;

    [Fact]
    public void A_dataset_reads_its_targets_a_store_with_a_prefix_and_a_directory()
    {
        Declarations declarations = T.Read(Orders);

        Assert.Empty(declarations.Findings);
        DatasetDeclaration orders = declarations.Datasets["orders"];
        Assert.Equal(2, orders.ShippingTargets.Count);
        ShippingTargetDeclaration replica = orders.ShippingTargets.Single(target => target.Store is not null);
        Assert.Equal("https://acct2.blob.core.windows.net/replicas", replica.Store);
        Assert.Equal("orders", replica.Prefix);
        Assert.Equal("azure-blob", replica.Provider);
        Assert.Equal("varve-2", replica.ShippedOn);
        Assert.Null(replica.Directory);
        ShippingTargetDeclaration archive = orders.ShippingTargets.Single(target => target.Directory is not null);
        Assert.Equal("/var/lib/varve/archive/orders", archive.Directory);
        Assert.Null(archive.Store);
        Assert.Null(archive.ShippedOn);
    }

    [Fact]
    public void A_dataset_with_no_target_reads_none()
    {
        Declarations declarations = T.Read("""
            <https://w3id.org/varve/ns#manage/dataset/orders> { n:orders a varve:Dataset ; varve:datasetName "orders" ; varve:storage varve:ObjectStorage . }
            """);

        Assert.Empty(declarations.Findings);
        Assert.Empty(declarations.Datasets["orders"].ShippingTargets);
    }

    [Theory]
    // A dataset on the file backend cannot be a shipping source (the maintainer's answer D10 on #166).
    [InlineData("varve:storage varve:ObjectStorage ;", "varve:storage varve:File ;", "the file backend cannot be a shipping source")]
    [InlineData("varve:storage varve:ObjectStorage ;", "", "the file backend cannot be a shipping source")]
    // A target names one place, in one of the two forms.
    [InlineData("varve:provider \"azure-blob\" ;", "", "varve:store, varve:prefix and varve:provider, or a varve:directory")]
    [InlineData("varve:prefix \"orders\" ;", "", "varve:store, varve:prefix and varve:provider, or a varve:directory")]
    [InlineData("varve:prefix \"orders\" ;", "varve:prefix \"orders\" ; varve:directory \"/replica\" ;", "not both")]
    [InlineData("varve:directory \"/var/lib/varve/archive/orders\"", "varve:directory \"\"", "varve:directory is a path")]
    // An address carries no credential (ADR 0037) and is https or s3.
    [InlineData("https://acct2.blob.core.windows.net/replicas", "https://acct2.blob.core.windows.net/replicas?sv=2024&sig=abc", "no query and no fragment")]
    [InlineData("https://acct2.blob.core.windows.net/replicas", "https://key@acct2.blob.core.windows.net/replicas", "no query and no fragment")]
    [InlineData("https://acct2.blob.core.windows.net/replicas", "http://acct2.blob.core.windows.net/replicas", "https://… or s3://…")]
    [InlineData("https://acct2.blob.core.windows.net/replicas", "/mnt/replicas", "https://… or s3://…")]
    [InlineData("varve:prefix \"orders\"", "varve:prefix \"orders/\"", "varve:prefix is one or more names")]
    [InlineData("varve:prefix \"orders\"", "varve:prefix \"../orders\"", "varve:prefix is one or more names")]
    [InlineData("varve:provider \"azure-blob\"", "varve:provider \"azure blob\"", "a provider's id on the compatibility page")]
    [InlineData("varve:shippedOn \"varve-2\"", "varve:shippedOn \"\"", "varve:shippedOn names an instance")]
    [InlineData("varve:shippedOn \"varve-2\"", "varve:shippedOn \"varve-2\" , \"varve-3\"", "stated at most once")]
    // The link and the type go together.
    [InlineData("varve:shipsTo n:replica , n:archive .", "varve:shipsTo n:replica .", "named by the dataset's varve:shipsTo")]
    [InlineData("n:archive a varve:ShippingTarget ;", "n:archive", "varve:shipsTo names a varve:ShippingTarget declared in this graph")]
    // Two targets do not name one place.
    [InlineData("n:archive a varve:ShippingTarget ; varve:directory \"/var/lib/varve/archive/orders\" .", "n:archive a varve:ShippingTarget ; varve:store \"https://acct2.blob.core.windows.net/replicas\" ; varve:prefix \"orders\" ; varve:provider \"azure-blob\" .", "do not name the same place")]
    public void A_target_that_is_not_one_place_of_an_object_store_dataset_is_refused(string from, string to, string says)
    {
        Assert.Contains(from, Orders, StringComparison.Ordinal);

        Declarations declarations = T.Read(Orders.Replace(from, to, StringComparison.Ordinal));

        Assert.Contains(declarations.Findings, finding => finding.Message.Contains(says, StringComparison.Ordinal));
    }
}
