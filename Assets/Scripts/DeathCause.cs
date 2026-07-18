public enum DeathCause
{
    Unknown,
    OldAge,
    Starvation,
    EnergyDepletion,
    GasToxicity,
    MassViabilityFloor,
    MediumMismatch,
    SexualIsolation,
    // Energy ran out while the agent was under severe thermal/atmospheric discomfort — i.e. the
    // climate (not mere bad foraging luck) drove the metabolic deficit that killed it. Split out
    // from EnergyDepletion so climate-driven mortality is distinguishable in death-cause stats.
    ClimateStress,
    // Killed by a Great Gas Event: the gas this agent breathes collapsed out of the atmosphere.
    // Only agents breathing the collapsed gas are at risk; AlternativeRespirationPathway-adapted
    // agents (breathing a different gas) survive, so adaptation is rewarded by selection.
    AtmosphericCollapse,
}
