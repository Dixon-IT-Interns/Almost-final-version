namespace Dixon.CommandCenter.API.Services;

public sealed record ViewerAccessScope(
    bool IsConfigured,
    bool AllPlants,
    bool AllCustomers,
    IReadOnlySet<int> PlantIds,
    IReadOnlySet<int> CustomerIds,
    IReadOnlySet<(int PlantId, int CustomerId)> PlantCustomerAssignments);

public static class ViewerAccessScopePolicy
{
    public static bool CanAccessPlant(ViewerAccessScope scope, int plantId) =>
        scope.IsConfigured
        && plantId > 0
        && (scope.AllPlants || scope.PlantIds.Contains(plantId));

    public static bool CanAccessCustomer(
        ViewerAccessScope scope,
        int plantId,
        int customerId,
        bool isActivePlantCustomerMapping) =>
        CanAccessPlant(scope, plantId)
        && customerId > 0
        && isActivePlantCustomerMapping
        && (scope.AllCustomers
            || (scope.AllPlants
                ? scope.CustomerIds.Contains(customerId)
                : scope.PlantCustomerAssignments.Contains((plantId, customerId))));
}
