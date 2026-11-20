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
            varve:shipsTo n:replica , n:second .
          n:replica a varve:ShippingTarget ;
            varve:store "https://acct2.blob.core.windows.net/replicas" ;
            varve:prefix "orders" ;
            varve:provider "azure-blob" ;
            varve:shippedOn "varve-2" .
          n:second a varve:ShippingTarget ; varve:store "s3://replicas" ; varve:prefix "copies/orders" ; varve:provider "rustfs" .
        }
        """;

    [Fact]
    public void A_dataset_reads_its_targets_each_a_store_and_a_prefix()
    {
        Declarations declarations = T.Read(Orders);

        Assert.Empty(declarations.Findings);
        DatasetDeclaration orders = declarations.Datasets["orders"];
        Assert.Equal(2, orders.ShippingTargets.Count);
        ShippingTargetDeclaration replica = orders.ShippingTargets.Single(target => target.Store.StartsWith("https://", StringComparison.Ordinal));
        Assert.Equal("https://acct2.blob.core.windows.net/replicas", replica.Store);
        Assert.Equal("orders", replica.Prefix);
        Assert.Equal("azure-blob", replica.Provider);
        Assert.Equal("varve-2", replica.ShippedOn);
        ShippingTargetDeclaration second = orders.ShippingTargets.Single(target => target.Store == "s3://replicas");
        Assert.Equal("copies/orders", second.Prefix);
        Assert.Equal("rustfs", second.Provider);
        Assert.Null(second.ShippedOn);
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
    [InlineData("varve:provider \"azure-blob\" ;", "", "it is a store and a prefix in it")]
    [InlineData("varve:prefix \"orders\" ;", "", "it is a store and a prefix in it")]
    // A directory is not a target (the maintainer's ruling of 2026-11-20 on #166): the term is not in the vocabulary.
    [InlineData("varve:prefix \"orders\" ;", "varve:prefix \"orders\" ; varve:directory \"/replica\" ;", "not a term of the management vocabulary")]
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
    [InlineData("varve:shipsTo n:replica , n:second .", "varve:shipsTo n:replica .", "named by the dataset's varve:shipsTo")]
    [InlineData("n:second a varve:ShippingTarget ;", "n:second", "varve:shipsTo names a varve:ShippingTarget declared in this graph")]
    // Two targets do not name one place.
    [InlineData("varve:store \"s3://replicas\" ; varve:prefix \"copies/orders\" ; varve:provider \"rustfs\"", "varve:store \"https://acct2.blob.core.windows.net/replicas\" ; varve:prefix \"orders\" ; varve:provider \"azure-blob\"", "do not name the same place")]
    public void A_target_that_is_not_one_place_of_an_object_store_dataset_is_refused(string from, string to, string says)
    {
        Assert.Contains(from, Orders, StringComparison.Ordinal);

        Declarations declarations = T.Read(Orders.Replace(from, to, StringComparison.Ordinal));

        Assert.Contains(declarations.Findings, finding => finding.Message.Contains(says, StringComparison.Ordinal));
    }
}
