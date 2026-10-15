// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Events.Model;

/// <summary>A type in a list: the type and the URLs of the dataset that declares it.</summary>
public sealed record ListedType(EventEndpoints Endpoints, CataloguedType Type);

/// <summary>The Event Hubs server an AsyncAPI document names, when the host configures one.</summary>
/// <param name="Host">The namespace's host, <c>{namespace}.servicebus.windows.net</c>.</param>
/// <param name="Hub">The event hub's name.</param>
[DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
public sealed record EventHubsServer(string Host, string Hub);
