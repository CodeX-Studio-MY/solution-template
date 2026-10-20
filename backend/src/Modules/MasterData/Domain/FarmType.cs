namespace Farm360.MasterData.Domain;

/// <summary>How a farm grows. Stored as a number: append new values, never reorder or remove one.</summary>
public enum FarmType
{
    Field = 0,
    Greenhouse = 1,
    Nursery = 2,
}
