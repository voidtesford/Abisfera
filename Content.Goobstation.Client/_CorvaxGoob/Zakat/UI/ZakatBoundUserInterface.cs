using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Goobstation.Client._CorvaxGoob.Zakat.UI;

/// <summary>
/// Both neural maps of «Закат»: the disease core («Рост») and the map of one host
/// (symptom branches around «порабощение», or the slave's mutation ring).
/// Names and costs only: no descriptions, no counters.
/// </summary>
[UsedImplicitly]
public sealed class ZakatBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    /// <summary>Node id of the host's central neuron «порабощение».</summary>
    public const int EnslaveId = 1000;

    /// <summary>Mutation node ids start here.</summary>
    public const int MutationBase = 2000;

    [ViewVariables]
    private ZakatNeuralWindow? _window;

    public ZakatNeuralWindow? Window => _window;

    private NetEntity? _host;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<ZakatNeuralWindow>();
        _window.Map.OnNodePressed += OnNodePressed;

        if (UiKey is ZakatUiKey.Shop)
        {
            _window.Title = Loc.GetString("zakat-shop-title");
            _window.Map.CenterTitle = Loc.GetString("zakat-shop-center");
        }
        else
        {
            _window.Title = Loc.GetString("zakat-host-title");
        }
    }

    private void OnNodePressed(int id)
    {
        if (UiKey is ZakatUiKey.Shop)
        {
            SendMessage(new ZakatBuyGrowthMessage());
            return;
        }

        if (_host is not { } host)
            return;

        if (id == EnslaveId)
            SendMessage(new ZakatEnslaveMessage(host));
        else if (id >= MutationBase)
            SendMessage(new ZakatBuyMutationMessage(host, (ZakatMutation) (id - MutationBase)));
        else
            SendMessage(new ZakatBuySymptomMessage(host, (ZakatSymptom) id));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (_window == null)
            return;

        switch (state)
        {
            case ZakatShopState shop:
                UpdateShop(_window, shop);
                break;
            case ZakatHostState host:
                UpdateHost(_window, host);
                break;
        }
    }

    private static string Biomass(float value) => ((int) MathF.Floor(value)).ToString();

    private static string Cost(float cost) => Loc.GetString("zakat-cost", ("cost", (int) cost));

    private static ZakatNodeState NodeState(bool bought, bool open) =>
        bought ? ZakatNodeState.Bought : open ? ZakatNodeState.Available : ZakatNodeState.Locked;

    /// <summary>
    /// The core is a single neuron: clicking it buys the next «Рост», and it grows with every level.
    /// Fully grown it stays bought (name only, no cost).
    /// </summary>
    public static ZakatNeuralNode BuildGrowthNode(ZakatShopState state)
    {
        if (state.Growth >= ZakatGrowth.MaxGrowth)
        {
            return new ZakatNeuralNode(ZakatGrowth.MaxGrowth, -1, 0,
                Loc.GetString("zakat-growth-level", ("level", ZakatGrowth.MaxGrowth)), string.Empty, ZakatNodeState.Bought, true);
        }

        var next = state.Growth + 1;
        var cost = ZakatGrowth.GrowthCosts[next - 1];
        // «Рост 4» opens only once enough flesh is captured
        var open = state.Hosts >= ZakatGrowth.RequiredHosts(next)
                   && (next != ZakatGrowth.VineLevel || state.Captured >= ZakatGrowth.VineTiles);
        return new ZakatNeuralNode(next, -1, 0, Loc.GetString("zakat-growth-level", ("level", next)), Cost(cost),
            open ? ZakatNodeState.Available : ZakatNodeState.Locked, state.Biomass >= cost);
    }

    /// <summary>
    /// Size of the core neuron at a given «Рост».
    /// </summary>
    public static float GrowthScale(int growth) => 1f + 0.38f * growth;

    public static (List<ZakatNeuralNode> Nodes, List<string> Branches) BuildHostNodes(ZakatHostState state)
    {
        var nodes = new List<ZakatNeuralNode>();
        var branches = new List<string>();
        for (var b = 0; b < ZakatSymptoms.Branches.Length; b++)
        {
            branches.Add(Loc.GetString($"zakat-branch-{b}"));
            var branch = ZakatSymptoms.Branches[b];
            for (var i = 0; i < branch.Length; i++)
            {
                var symptom = branch[i];
                var cost = ZakatSymptoms.Get(symptom).Cost;
                var open = i == 0 || state.Bought.Contains(branch[i - 1]);
                nodes.Add(new ZakatNeuralNode((int) symptom, b, i, Loc.GetString(ZakatSymptoms.LocId(symptom)), Cost(cost),
                    NodeState(state.Bought.Contains(symptom), open), state.Biomass >= cost));
            }
        }

        return (nodes, branches);
    }

    /// <summary>
    /// A slave's mutations: a ring, any node in any order.
    /// </summary>
    public static List<ZakatNeuralNode> BuildMutationNodes(ZakatHostState state)
    {
        var nodes = new List<ZakatNeuralNode>();
        for (var i = 0; i < ZakatGrowth.Mutations.Length; i++)
        {
            var mutation = ZakatGrowth.Mutations[i];
            var cost = ZakatGrowth.Cost(mutation);
            nodes.Add(new ZakatNeuralNode(MutationBase + (int) mutation, 0, i, Loc.GetString(ZakatGrowth.LocId(mutation)), Cost(cost),
                NodeState(state.Mutations.Contains(mutation), true), state.Biomass >= cost));
        }

        return nodes;
    }

    public static ZakatNeuralNode BuildEnslaveNode(ZakatHostState state)
    {
        return new ZakatNeuralNode(EnslaveId, -1, 0, Loc.GetString("zakat-enslave"), Cost(ZakatGrowth.EnslaveCost),
            NodeState(state.Enslaved, state.Growth >= ZakatGrowth.HiveMindLevel), state.Biomass >= ZakatGrowth.EnslaveCost);
    }

    private static void UpdateShop(ZakatNeuralWindow window, ZakatShopState state)
    {
        // no branches: only the core itself, bigger with every «Рост»
        window.Map.Ring = true;
        window.Map.SetNodes(new List<ZakatNeuralNode>(), new List<string>());
        window.Map.CenterNode = BuildGrowthNode(state);
        window.Map.CenterScale = GrowthScale(state.Growth);
        window.Map.Awakened = state.Growth >= ZakatGrowth.VineLevel;
        // locked levels show only the name and the cost
        window.Map.CenterSubtitle = string.Empty;
        window.Header.Text = Loc.GetString("zakat-header", ("biomass", Biomass(state.Biomass)));
    }

    private void UpdateHost(ZakatNeuralWindow window, ZakatHostState state)
    {
        _host = state.Host;
        var map = window.Map;
        map.CenterTitle = state.HostName;
        map.CenterScale = 1f;
        map.Awakened = false;

        if (state.Praetorian)
        {
            // an evolved slave is final: nothing more to grow
            map.Ring = true;
            map.CenterNode = null;
            map.CenterSubtitle = Loc.GetString("zakat-host-praetorian");
            map.SetNodes(new List<ZakatNeuralNode>(), new List<string>());
        }
        else if (state.Enslaved)
        {
            map.Ring = true;
            map.CenterNode = null;
            map.CenterSubtitle = Loc.GetString("zakat-slave");
            map.SetNodes(BuildMutationNodes(state), new List<string>());
        }
        else
        {
            var (nodes, branches) = BuildHostNodes(state);
            var enslave = BuildEnslaveNode(state);
            map.Ring = false;
            map.CenterNode = enslave;
            map.CenterSubtitle = Loc.GetString("zakat-enslave-caption", ("cost", (int) ZakatGrowth.EnslaveCost));
            map.SetNodes(nodes, branches);
        }

        window.Header.Text = Loc.GetString("zakat-header", ("biomass", Biomass(state.Biomass)));
    }
}
