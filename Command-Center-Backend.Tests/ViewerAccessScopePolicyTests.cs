using Dixon.CommandCenter.API.Services;
using Xunit;

namespace Dixon.CommandCenter.API.Tests;

public sealed class ViewerAccessScopePolicyTests
{
    [Fact]
    public void AllPlantsAndAllCustomersIncludesFuturePlantsAndFutureMappings()
    {
        var scope = Scope(allPlants: true, allCustomers: true);

        Assert.True(ViewerAccessScopePolicy.CanAccessPlant(scope, 900));
        Assert.True(ViewerAccessScopePolicy.CanAccessCustomer(scope, 900, 700, true));
    }

    [Fact]
    public void SpecificPlantsAndAllCustomersIncludesAnyMappedCustomerAtThosePlants()
    {
        var scope = Scope(allCustomers: true, plants: [1, 2]);

        Assert.True(ViewerAccessScopePolicy.CanAccessCustomer(scope, 2, 55, true));
        Assert.False(ViewerAccessScopePolicy.CanAccessCustomer(scope, 3, 55, true));
    }

    [Fact]
    public void SpecificPlantsAndCustomersRequiresBothAssignmentAndMapping()
    {
        var scope = Scope(
            plants: [1, 2],
            plantCustomers: [(2, 55), (2, 56)]);

        Assert.True(ViewerAccessScopePolicy.CanAccessCustomer(scope, 2, 55, true));
        Assert.False(ViewerAccessScopePolicy.CanAccessCustomer(scope, 1, 55, true));
        Assert.False(ViewerAccessScopePolicy.CanAccessCustomer(scope, 1, 56, true));
        Assert.True(ViewerAccessScopePolicy.CanAccessCustomer(scope, 2, 56, true));
        Assert.False(ViewerAccessScopePolicy.CanAccessCustomer(scope, 2, 55, false));
    }

    [Fact]
    public void AllPlantsAndSpecificCustomerAllowsEveryPlantWhereMappingExists()
    {
        var scope = Scope(allPlants: true, customers: [55]);

        Assert.True(ViewerAccessScopePolicy.CanAccessCustomer(scope, 1, 55, true));
        Assert.True(ViewerAccessScopePolicy.CanAccessCustomer(scope, 900, 55, true));
        Assert.False(ViewerAccessScopePolicy.CanAccessCustomer(scope, 900, 56, true));
        Assert.False(ViewerAccessScopePolicy.CanAccessCustomer(scope, 900, 55, false));
    }

    [Fact]
    public void UnconfiguredOrDeletedAssignmentsGrantNoAccess()
    {
        var unconfigured = Scope(configured: false, allPlants: true, allCustomers: true);
        var missingSelections = Scope(plants: [], customers: []);

        Assert.False(ViewerAccessScopePolicy.CanAccessPlant(unconfigured, 1));
        Assert.False(ViewerAccessScopePolicy.CanAccessCustomer(unconfigured, 1, 1, true));
        Assert.False(ViewerAccessScopePolicy.CanAccessPlant(missingSelections, 1));
        Assert.False(ViewerAccessScopePolicy.CanAccessCustomer(missingSelections, 1, 1, true));
    }

    private static ViewerAccessScope Scope(
        bool configured = true,
        bool allPlants = false,
        bool allCustomers = false,
        int[]? plants = null,
        int[]? customers = null,
        (int PlantId, int CustomerId)[]? plantCustomers = null) =>
        new(
            configured,
            allPlants,
            allCustomers,
            (plants ?? []).ToHashSet(),
            (customers ?? []).ToHashSet(),
            (plantCustomers ?? []).ToHashSet());
}
