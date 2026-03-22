namespace Chiro.App.Models;

/// <summary>
/// Defines the type of an ecological zone.
/// ZNIEFF Type I: Small areas with high ecological interest (rare/endangered species, specific habitats).
/// ZNIEFF Type II: Larger areas with good overall biological balance, often containing Type I zones.
/// Natura 2000 ZPS: Zone de Protection Spéciale (Birds Directive).
/// Natura 2000 ZSC: Zone Spéciale de Conservation (Habitats Directive).
/// Natura 2000 SIC: Site d'Importance Communautaire (candidate ZSC).
/// EP: Espaces Protégés (various national/regional protection designations).
/// </summary>
public enum ZoneType
{
    Other = 0,
    Znieff1 = 1,
    Znieff2 = 2,
    Natura2000Zps = 3,
    Natura2000Zsc = 4,
    Natura2000Sic = 5,
    EspaceProtege = 6
}
