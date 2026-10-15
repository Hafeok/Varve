// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Varve.Rec.Tests;

/// <summary>The named portfolio sizes: the stated datasets for benchmarks (ADR 0141).</summary>
public enum RecSize
{
    /// <summary>About 200,000 quads at the head, about 1,000 commits: 25 buildings.</summary>
    S,

    /// <summary>About 5,000,000 quads at the head, about 25,000 commits: 622 buildings.</summary>
    M,

    /// <summary>About 50,000,000 quads at the head, about 100,000 commits: 6,220 buildings.</summary>
    L,
}

/// <summary>A quad as N-Triples terms: <c>&lt;iri&gt;</c> or a quoted literal, and the graph's IRI.</summary>
public readonly record struct RecQuad(string Subject, string Predicate, string Object, string Graph);

/// <summary>One commit of a generated history: what it is, what it asserts and what it retracts.</summary>
public sealed class RecCommit(string kind, List<RecQuad> asserted, List<RecQuad> retracted)
{
    /// <summary>What happened: <c>build</c>, <c>retype</c>, <c>split</c>, <c>merge</c>, <c>move</c>, <c>point-added</c>, <c>point-retired</c>, <c>tenant-in</c>, <c>tenant-out</c>.</summary>
    public string Kind { get; } = kind;

    public IReadOnlyList<RecQuad> Asserted { get; } = asserted;

    public IReadOnlyList<RecQuad> Retracted { get; } = retracted;
}

/// <summary>
/// A seeded, deterministic generator of RealEstateCore 4 portfolios with a
/// history (ADR 0141): regions, sites, buildings, levels, rooms, assets and
/// points, built floor by floor, then renovated — rooms split, merged and
/// retyped — with assets moved, points added and retired, and tenants
/// entering and leaving by lease.
/// </summary>
/// <remarks>
/// <para>
/// Test-only and never shipped. Its classes and properties are REC 4's
/// (<c>testdata/rec/v4.1.0/rec.ttl</c>) and, for points, Brick's:
/// <c>rec:hasPart</c> and <c>rec:isPartOf</c> down the topology,
/// <c>rec:locatedIn</c> and <c>rec:isLocationOf</c> for assets,
/// <c>rec:hasPoint</c>, and <c>rec:Lease</c> with <c>rec:leaseOf</c> and
/// <c>rec:leasee</c>. Each site's quads are in a graph of their own.
/// </para>
/// <para>
/// **The same seed and size give the same bytes on every platform**: the
/// random sequence is SplitMix64, written here, every choice is integer
/// arithmetic, and every string is built with the invariant culture.
/// </para>
/// </remarks>
public static class RecPortfolio
{
    private const string Rec = "https://w3id.org/rec#";
    private const string Brick = "https://brickschema.org/schema/Brick#";
    private const string Data = "https://example.org/rec/";
    private const string Type = "<http://www.w3.org/1999/02/22-rdf-syntax-ns#type>";
    private const string Label = "<http://www.w3.org/2000/01/rdf-schema#label>";

    private const int Levels = 8;
    private const int RoomsPerLevel = 25;
    private const int AssetsPerRoom = 3;
    private const int PointsPerAsset = 2;
    private const int PointsPerRoom = 2;
    private const int BuildingsPerSite = 10;
    private const int SitesPerRegion = 10;

    /// <summary>How many buildings and commits a size has.</summary>
    public static (int Buildings, int Commits) Shape(RecSize size) => size switch
    {
        RecSize.S => (25, 1_000),
        RecSize.M => (622, 25_000),
        RecSize.L => (6_220, 100_000),
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, "No such size."),
    };

    /// <summary>The history, commit by commit, generated as it is read.</summary>
    public static IEnumerable<RecCommit> Generate(RecSize size, ulong seed)
    {
        (int buildings, int commits) = Shape(size);
        return new Generator(seed).Run(buildings, commits);
    }

    /// <summary>
    /// The history in its canonical bytes: per commit a line
    /// <c># commit n kind</c>, then <c>+ s p o g .</c> for each assertion and
    /// <c>- s p o g .</c> for each retraction, in the order generated, UTF-8,
    /// <c>\n</c> line ends.
    /// </summary>
    public static void Write(IEnumerable<RecCommit> history, Stream output)
    {
        using StreamWriter writer = new(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1 << 16, leaveOpen: true) { NewLine = "\n" };
        int number = 0;

        foreach (RecCommit commit in history)
        {
            writer.Write("# commit ");
            writer.Write((++number).ToString(CultureInfo.InvariantCulture));
            writer.Write(' ');
            writer.Write(commit.Kind);
            writer.Write('\n');

            foreach (RecQuad quad in commit.Asserted)
            {
                Line(writer, '+', quad);
            }

            foreach (RecQuad quad in commit.Retracted)
            {
                Line(writer, '-', quad);
            }
        }
    }

    private static void Line(StreamWriter writer, char sign, RecQuad quad)
    {
        writer.Write(sign);
        writer.Write(' ');
        writer.Write(quad.Subject);
        writer.Write(' ');
        writer.Write(quad.Predicate);
        writer.Write(' ');
        writer.Write(quad.Object);
        writer.Write(' ');
        writer.Write(quad.Graph);
        writer.Write(" .\n");
    }

    private static string Iri(string kind, int id) => "<" + Data + kind + "/" + id.ToString(CultureInfo.InvariantCulture) + ">";

    private static string RecClass(string name) => "<" + Rec + name + ">";

    private static string RecProperty(string name) => "<" + Rec + name + ">";

    private static string BrickClass(string name) => "<" + Brick + name + ">";

    private static string Literal(string text) => "\"" + text + "\"";

    /// <summary>A set of ids that can hand out a random member and drop one, both in constant time.</summary>
    private sealed class Alive
    {
        private readonly List<int> _items = [];
        private readonly Dictionary<int, int> _at = [];

        public int Count => _items.Count;

        public void Add(int id)
        {
            _at[id] = _items.Count;
            _items.Add(id);
        }

        public void Remove(int id)
        {
            int index = _at[id];
            int last = _items[^1];
            _items[index] = last;
            _at[last] = index;
            _items.RemoveAt(_items.Count - 1);
            _at.Remove(id);
        }

        public int Pick(SplitMix64 random) => _items[random.Below(_items.Count)];
    }

    private sealed class Generator(ulong seed)
    {
        // REC 4's classes for rooms, assets and buildings, and Brick's for points.
        private readonly string[] RoomTypes = ["Office", "ConferenceRoom", "Kitchenette", "Bathroom", "Hallway", "OfficeLandscape", "PhoneBooth", "Pantry"];
        private readonly string[] AssetTypes = ["Desk", "OfficeChair", "Cabinet", "OccupancySensorEquipment", "IAQSensorEquipment", "Lamp"];
        private readonly string[] PointTypes = ["Temperature_Sensor", "CO2_Sensor", "Humidity_Sensor", "Occupancy_Count_Sensor", "Air_Temperature_Setpoint"];
        private readonly string[] BuildingTypes = ["Building", "Building", "Building", "School", "Hospital", "ShoppingMall"];

        private readonly SplitMix64 _random = new(seed);

        // Rooms.
        private readonly List<int> _roomLevel = [];
        private readonly List<byte> _roomType = [];
        private readonly List<List<int>> _roomAssets = [];
        private readonly List<List<int>> _roomPoints = [];
        private readonly Alive _rooms = new();

        // Levels and buildings: a level's building, a building's site, a level's live rooms.
        private readonly List<int> _levelBuilding = [];
        private readonly List<Alive> _levelRooms = [];
        private readonly List<int> _buildingSite = [];
        private readonly List<int> _buildingFirstLevel = [];

        // Assets and points.
        private readonly List<int> _assetRoom = [];
        private readonly List<byte> _assetType = [];
        private readonly List<List<int>> _assetPoints = [];
        private readonly Alive _assets = new();
        private readonly List<string> _pointHost = [];
        private readonly List<byte> _pointType = [];
        private readonly List<int> _pointSite = [];
        private readonly List<bool> _pointLive = [];
        private readonly Alive _points = new();

        // Leases: the company, the rooms.
        private readonly List<int> _leaseCompany = [];
        private readonly List<int[]> _leaseRooms = [];
        private readonly List<int> _leaseSite = [];
        private readonly Alive _leases = new();
        private int _companies;

        public IEnumerable<RecCommit> Run(int buildings, int commits)
        {
            int made = 0;

            for (int building = 0; building < buildings; building++)
            {
                foreach (RecCommit commit in Build(building))
                {
                    made++;
                    yield return commit;
                }
            }

            while (made < commits)
            {
                made++;
                yield return Event();
            }
        }

        private static string Graph(int site) => "<" + Data + "graph/site/" + site.ToString(CultureInfo.InvariantCulture) + ">";

        // One commit for the building and its place in the topology, then one per level.
        private IEnumerable<RecCommit> Build(int building)
        {
            int site = building / BuildingsPerSite;
            int region = site / SitesPerRegion;
            string graph = Graph(site);
            List<RecQuad> top = [];

            if (building % BuildingsPerSite == 0)
            {
                if (site % SitesPerRegion == 0)
                {
                    top.Add(new(Iri("region", region), Type, RecClass("Region"), graph));
                    top.Add(new(Iri("region", region), Label, Literal("Region " + region.ToString(CultureInfo.InvariantCulture)), graph));
                }

                top.Add(new(Iri("site", site), Type, RecClass("Site"), graph));
                top.Add(new(Iri("site", site), Label, Literal("Site " + site.ToString(CultureInfo.InvariantCulture)), graph));
                Link(top, Iri("region", region), Iri("site", site), graph);
            }

            top.Add(new(Iri("building", building), Type, RecClass(_random.Pick(BuildingTypes)), graph));
            top.Add(new(Iri("building", building), Label, Literal("Building " + building.ToString(CultureInfo.InvariantCulture)), graph));
            Link(top, Iri("site", site), Iri("building", building), graph);
            _buildingSite.Add(site);
            _buildingFirstLevel.Add(_levelBuilding.Count);
            yield return new RecCommit("build", top, []);

            for (int level = 0; level < Levels; level++)
            {
                int id = _levelBuilding.Count;
                _levelBuilding.Add(building);
                _levelRooms.Add(new Alive());
                List<RecQuad> quads = [];
                quads.Add(new(Iri("level", id), Type, RecClass("Level"), graph));
                quads.Add(new(Iri("level", id), Label, Literal("Level " + level.ToString(CultureInfo.InvariantCulture)), graph));
                Link(quads, Iri("building", building), Iri("level", id), graph);

                for (int room = 0; room < RoomsPerLevel; room++)
                {
                    int roomId = NewRoom(quads, id, site);

                    for (int asset = 0; asset < AssetsPerRoom; asset++)
                    {
                        int assetId = NewAsset(quads, roomId, site);

                        for (int point = 0; point < PointsPerAsset; point++)
                        {
                            NewPoint(quads, Iri("asset", assetId), site, _assetPoints[assetId]);
                        }
                    }

                    for (int point = 0; point < PointsPerRoom; point++)
                    {
                        NewPoint(quads, Iri("room", roomId), site, _roomPoints[roomId]);
                    }
                }

                yield return new RecCommit("build", quads, []);
            }
        }

        private static void Link(List<RecQuad> quads, string whole, string part, string graph)
        {
            quads.Add(new(whole, RecProperty("hasPart"), part, graph));
            quads.Add(new(part, RecProperty("isPartOf"), whole, graph));
        }

        private int NewRoom(List<RecQuad> quads, int level, int site)
        {
            int id = _roomLevel.Count;
            byte type = (byte)_random.Below(RoomTypes.Length);
            _roomLevel.Add(level);
            _roomType.Add(type);
            _roomAssets.Add([]);
            _roomPoints.Add([]);
            _rooms.Add(id);
            _levelRooms[level].Add(id);
            string graph = Graph(site);
            quads.Add(new(Iri("room", id), Type, RecClass(RoomTypes[type]), graph));
            quads.Add(new(Iri("room", id), Label, Literal("Room " + id.ToString(CultureInfo.InvariantCulture)), graph));
            Link(quads, Iri("level", level), Iri("room", id), graph);
            return id;
        }

        private int NewAsset(List<RecQuad> quads, int room, int site)
        {
            int id = _assetRoom.Count;
            byte type = (byte)_random.Below(AssetTypes.Length);
            _assetRoom.Add(room);
            _assetType.Add(type);
            _assetPoints.Add([]);
            _assets.Add(id);
            _roomAssets[room].Add(id);
            string graph = Graph(site);
            quads.Add(new(Iri("asset", id), Type, RecClass(AssetTypes[type]), graph));
            quads.Add(new(Iri("asset", id), Label, Literal("Asset " + id.ToString(CultureInfo.InvariantCulture)), graph));
            Located(quads, id, room, graph);
            return id;
        }

        private static void Located(List<RecQuad> quads, int asset, int room, string graph)
        {
            quads.Add(new(Iri("asset", asset), RecProperty("locatedIn"), Iri("room", room), graph));
            quads.Add(new(Iri("room", room), RecProperty("isLocationOf"), Iri("asset", asset), graph));
        }

        private void NewPoint(List<RecQuad> quads, string host, int site, List<int> hostPoints)
        {
            int id = _pointHost.Count;
            byte type = (byte)_random.Below(PointTypes.Length);
            _pointHost.Add(host);
            _pointType.Add(type);
            _pointSite.Add(site);
            _pointLive.Add(true);
            _points.Add(id);
            hostPoints.Add(id);
            PointQuads(quads, id);
        }

        private void PointQuads(List<RecQuad> quads, int point)
        {
            string graph = Graph(_pointSite[point]);
            quads.Add(new(Iri("point", point), Type, BrickClass(PointTypes[_pointType[point]]), graph));
            quads.Add(new(Iri("point", point), Label, Literal("Point " + point.ToString(CultureInfo.InvariantCulture)), graph));
            quads.Add(new(_pointHost[point], RecProperty("hasPoint"), Iri("point", point), graph));
        }

        private int SiteOfRoom(int room) => _buildingSite[_levelBuilding[_roomLevel[room]]];

        private RecCommit Event()
        {
            int roll = _random.Below(100);

            return roll switch
            {
                < 5 => Retype(),
                < 20 => Split(),
                < 22 => Merge(),
                < 35 => Move(),
                < 70 => AddPoint(),
                < 73 => RetirePoint(),
                < 95 => TenantIn(),
                _ => TenantOut(),
            };
        }

        private RecCommit Retype()
        {
            int room = _rooms.Pick(_random);
            string graph = Graph(SiteOfRoom(room));
            byte type = (byte)_random.Below(RoomTypes.Length);

            if (type == _roomType[room])
            {
                type = (byte)((type + 1) % RoomTypes.Length);
            }

            List<RecQuad> retracted = [new(Iri("room", room), Type, RecClass(RoomTypes[_roomType[room]]), graph)];
            _roomType[room] = type;
            return new RecCommit("retype", [new(Iri("room", room), Type, RecClass(RoomTypes[type]), graph)], retracted);
        }

        // A room divided by a new wall: a new room on the same level, and an
        // asset carried into it.
        private RecCommit Split()
        {
            int room = _rooms.Pick(_random);
            int site = SiteOfRoom(room);
            string graph = Graph(site);
            List<RecQuad> asserted = [];
            List<RecQuad> retracted = [];
            int added = NewRoom(asserted, _roomLevel[room], site);

            if (_roomAssets[room].Count > 0)
            {
                int asset = _roomAssets[room][_random.Below(_roomAssets[room].Count)];
                MoveAsset(asset, added, graph, asserted, retracted);
            }

            return new RecCommit("split", asserted, retracted);
        }

        // A wall taken down: a second room on the level goes, its assets come
        // into the first, its points are retired, and any lease of it ends.
        private RecCommit Merge()
        {
            int room = _rooms.Pick(_random);
            Alive siblings = _levelRooms[_roomLevel[room]];

            if (siblings.Count < 2)
            {
                return Retype();
            }

            int gone;

            do
            {
                gone = siblings.Pick(_random);
            }
            while (gone == room);

            if (Leased(gone))
            {
                return Retype();
            }

            string graph = Graph(SiteOfRoom(room));
            List<RecQuad> asserted = [];
            List<RecQuad> retracted = [];

            foreach (int asset in _roomAssets[gone].ToArray())
            {
                MoveAsset(asset, room, graph, asserted, retracted);
            }

            foreach (int point in _roomPoints[gone])
            {
                if (_pointLive[point])
                {
                    PointQuads(retracted, point);
                    _points.Remove(point);
                    _pointLive[point] = false;
                }
            }

            _roomPoints[gone].Clear();
            retracted.Add(new(Iri("room", gone), Type, RecClass(RoomTypes[_roomType[gone]]), graph));
            retracted.Add(new(Iri("room", gone), Label, Literal("Room " + gone.ToString(CultureInfo.InvariantCulture)), graph));
            retracted.Add(new(Iri("level", _roomLevel[gone]), RecProperty("hasPart"), Iri("room", gone), graph));
            retracted.Add(new(Iri("room", gone), RecProperty("isPartOf"), Iri("level", _roomLevel[gone]), graph));
            _rooms.Remove(gone);
            siblings.Remove(gone);
            return new RecCommit("merge", asserted, retracted);
        }

        private bool Leased(int room)
        {
            for (int i = 0; i < _leaseRooms.Count; i++)
            {
                if (Array.IndexOf(_leaseRooms[i], room) >= 0 && _leaseCompany[i] >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        // An asset carried to another room of the same building.
        private RecCommit Move()
        {
            int asset = _assets.Pick(_random);
            int from = _assetRoom[asset];
            int building = _levelBuilding[_roomLevel[from]];
            Alive rooms = _levelRooms[_buildingFirstLevel[building] + _random.Below(Levels)];

            if (rooms.Count == 0)
            {
                return Retype();
            }

            int to = rooms.Pick(_random);

            if (to == from)
            {
                return Retype();
            }

            List<RecQuad> asserted = [];
            List<RecQuad> retracted = [];
            MoveAsset(asset, to, Graph(SiteOfRoom(from)), asserted, retracted);
            return new RecCommit("move", asserted, retracted);
        }

        private void MoveAsset(int asset, int to, string graph, List<RecQuad> asserted, List<RecQuad> retracted)
        {
            int from = _assetRoom[asset];
            Located(retracted, asset, from, graph);
            Located(asserted, asset, to, graph);
            _roomAssets[from].Remove(asset);
            _roomAssets[to].Add(asset);
            _assetRoom[asset] = to;
        }

        private RecCommit AddPoint()
        {
            int asset = _assets.Pick(_random);
            List<RecQuad> asserted = [];
            NewPoint(asserted, Iri("asset", asset), SiteOfRoom(_assetRoom[asset]), _assetPoints[asset]);
            return new RecCommit("point-added", asserted, []);
        }

        private RecCommit RetirePoint()
        {
            int point = _points.Pick(_random);
            List<RecQuad> retracted = [];
            PointQuads(retracted, point);
            _points.Remove(point);
            _pointLive[point] = false;
            return new RecCommit("point-retired", [], retracted);
        }

        // A tenant signs a lease for one to five rooms of a level.
        private RecCommit TenantIn()
        {
            int room = _rooms.Pick(_random);
            Alive siblings = _levelRooms[_roomLevel[room]];
            int count = 1 + _random.Below(5);
            HashSet<int> rooms = [room];

            for (int i = 0; i < count && rooms.Count < siblings.Count; i++)
            {
                rooms.Add(siblings.Pick(_random));
            }

            int[] leased = [.. rooms];
            Array.Sort(leased);
            int site = SiteOfRoom(room);
            string graph = Graph(site);
            int company = _companies++;
            int lease = _leaseCompany.Count;
            _leaseCompany.Add(company);
            _leaseRooms.Add(leased);
            _leaseSite.Add(site);
            _leases.Add(lease);
            return new RecCommit("tenant-in", LeaseQuads(lease), []);
        }

        private List<RecQuad> LeaseQuads(int lease)
        {
            string graph = Graph(_leaseSite[lease]);
            int company = _leaseCompany[lease];
            List<RecQuad> quads =
            [
                new(Iri("company", company), Type, RecClass("Company"), graph),
                new(Iri("company", company), Label, Literal("Tenant " + company.ToString(CultureInfo.InvariantCulture)), graph),
                new(Iri("lease", lease), Type, RecClass("Lease"), graph),
                new(Iri("lease", lease), RecProperty("leasee"), Iri("company", company), graph),
            ];

            foreach (int room in _leaseRooms[lease])
            {
                quads.Add(new(Iri("lease", lease), RecProperty("leaseOf"), Iri("room", room), graph));
            }

            return quads;
        }

        private RecCommit TenantOut()
        {
            if (_leases.Count == 0)
            {
                return TenantIn();
            }

            int lease = _leases.Pick(_random);
            List<RecQuad> retracted = LeaseQuads(lease);
            _leases.Remove(lease);
            _leaseCompany[lease] = -1;
            return new RecCommit("tenant-out", [], retracted);
        }
    }
}
