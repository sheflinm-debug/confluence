/// Real terrain archetypes (Section 3 Layer 1 stand-in), classified from continuous
/// temperature/moisture fields rather than an abstract pressure label.
public enum BiomeType
{
    Desert,
    Jungle,
    Forest,
    Grassland,
    Tundra,
    Wetland // coastal/swamp-like - very high moisture overrides temperature classification
}
