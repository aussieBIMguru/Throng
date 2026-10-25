using Throng.Core.Library;
using Throng.Sim.Nav;

namespace Throng.Sim.Tests;

/// <summary>Profile preference for mechanised transport in route choice (D-138).</summary>
[TestClass]
public sealed class MechanisedPreferenceTests
{
    [TestMethod]
    public void Factor_Neutral_IsOne()
    {
        var profile = new AgentProfileTemplate();
        Assert.AreEqual(1.0, RoutePlanner.MechanisedFactor(ConnectorKind.Escalator, profile), 1e-9);
        Assert.AreEqual(1.0, RoutePlanner.MechanisedFactor(ConnectorKind.Escalator, null), 1e-9);
    }

    [TestMethod]
    public void Factor_Preferring_MakesBeltsFeelShorter_AvoidingLonger()
    {
        var keen = new AgentProfileTemplate { MechanisedPreference = 1.0 };
        var averse = new AgentProfileTemplate { MechanisedPreference = -1.0 };
        Assert.AreEqual(0.6, RoutePlanner.MechanisedFactor(ConnectorKind.Escalator, keen), 1e-9);
        Assert.AreEqual(0.6, RoutePlanner.MechanisedFactor(ConnectorKind.Travelator, keen), 1e-9);
        Assert.AreEqual(1.4, RoutePlanner.MechanisedFactor(ConnectorKind.Escalator, averse), 1e-9);
    }

    [TestMethod]
    public void Factor_StairsAndRamps_Unaffected_AndOutOfRangeClamped()
    {
        var keen = new AgentProfileTemplate { MechanisedPreference = 5.0 };
        Assert.AreEqual(1.0, RoutePlanner.MechanisedFactor(ConnectorKind.Stair, keen), 1e-9);
        Assert.AreEqual(1.0, RoutePlanner.MechanisedFactor(ConnectorKind.Ramp, keen), 1e-9);
        Assert.AreEqual(0.6, RoutePlanner.MechanisedFactor(ConnectorKind.Escalator, keen), 1e-9);
    }

    [TestMethod]
    public void BuiltIns_LuggageAndElderly_PreferBelts_CommuterDoesNot()
    {
        double Pref(string key) => ((AgentProfileTemplate)BuiltInLibrary.Instance.FindByKey(key)!).MechanisedPreference;
        Assert.IsTrue(Pref("agent.luggage") > 0.5);
        Assert.IsTrue(Pref("agent.elderly") > 0.3);
        Assert.IsTrue(Pref("agent.commuter") <= 0);
    }
}
