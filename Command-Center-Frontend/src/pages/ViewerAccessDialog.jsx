import { useEffect, useMemo, useState } from "react";
import { Save, X } from "lucide-react";
import { api } from "../api";
import "./ViewerAccessDialog.css";

export default function ViewerAccessDialog({
  user,
  plants,
  customers,
  plantCustomers,
  superAdmin,
  onClose,
  onSaved,
}) {
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [original, setOriginal] = useState(null);
  const [form, setForm] = useState({
    allPlants: false,
    allCustomers: false,
    plantIds: [],
    customerIds: [],
    plantCustomerAssignments: [],
  });
  const activePlants = useMemo(
    () => plants.filter(plant => plant.isActive),
    [plants],
  );
  const effectivePlantIds = form.allPlants
    ? activePlants.map(plant => Number(plant.plantId))
    : form.plantIds.map(Number);
  const mappedCustomerIds = new Set(plantCustomers
    .filter(mapping => effectivePlantIds.includes(Number(mapping.plantId)))
    .map(mapping => Number(mapping.customerId)));
  const availableCustomers = customers.filter(customer =>
    customer.isActive && mappedCustomerIds.has(Number(customer.customerId)));

  useEffect(() => {
    let active = true;
    api.get(`/admin/users/${user.userId}/viewer-access`)
      .then(configuration => {
        if (!active) return;
        const validPlantIds = (configuration.plantIds || [])
          .filter(id => activePlants.some(plant => Number(plant.plantId) === Number(id)))
          .map(Number);
        const validMappings = new Set(plantCustomers.map(mapping =>
          `${Number(mapping.plantId)}:${Number(mapping.customerId)}`));
        const savedAssignments = configuration.plantCustomerAssignments?.length
          ? configuration.plantCustomerAssignments
          : validPlantIds.flatMap(plantId =>
            (configuration.customerIds || []).map(customerId => ({
              plantId,
              customerId: Number(customerId),
            })));
        const plantCustomerAssignments = savedAssignments
          .filter(assignment =>
            validPlantIds.includes(Number(assignment.plantId))
            && validMappings.has(`${Number(assignment.plantId)}:${Number(assignment.customerId)}`)
            && customers.some(customer =>
              Number(customer.customerId) === Number(assignment.customerId) && customer.isActive))
          .map(assignment => ({
            plantId: Number(assignment.plantId),
            customerId: Number(assignment.customerId),
          }))
          .sort((a, b) => a.plantId - b.plantId || a.customerId - b.customerId);
        const validGlobalCustomerIds = (configuration.customerIds || [])
          .map(Number)
          .filter(id => customers.some(customer =>
            Number(customer.customerId) === id
            && customer.isActive
            && plantCustomers.some(mapping =>
              Number(mapping.customerId) === id
              && activePlants.some(plant => Number(plant.plantId) === Number(mapping.plantId)))));
        const initial = {
          allPlants: Boolean(configuration.allPlants),
          allCustomers: Boolean(configuration.allCustomers),
          plantIds: configuration.allPlants
            ? []
            : validPlantIds.length
              ? validPlantIds
              : [Number(user.plantId)].filter(id =>
                activePlants.some(plant => Number(plant.plantId) === id)),
          customerIds: configuration.allCustomers || !configuration.allPlants
            ? []
            : validGlobalCustomerIds,
          plantCustomerAssignments: configuration.allCustomers || configuration.allPlants
            ? []
            : plantCustomerAssignments,
        };
        setOriginal({
          configured: Boolean(configuration.configured),
          allPlants: Boolean(configuration.allPlants),
          allCustomers: Boolean(configuration.allCustomers),
          plantIds: (configuration.plantIds || []).map(Number).sort((a, b) => a - b),
          customerIds: configuration.allPlants
            ? validGlobalCustomerIds.sort((a, b) => a - b)
            : [],
          plantCustomerAssignments: configuration.allPlants
            ? []
            : plantCustomerAssignments,
        });
        setForm(initial);
      })
      .catch(exception => {
        if (active) setError(exception.message || "Unable to load Viewer access.");
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => { active = false; };
  }, [user.userId, user.plantId, activePlants, customers, plantCustomers]);

  function changePlants(plantId, selected) {
    setForm(current => ({
      ...current,
      plantIds: selected
        ? [...new Set([...current.plantIds, Number(plantId)])]
        : current.plantIds.filter(id => Number(id) !== Number(plantId)),
      plantCustomerAssignments: selected
        ? current.plantCustomerAssignments
        : current.plantCustomerAssignments.filter(assignment =>
          Number(assignment.plantId) !== Number(plantId)),
    }));
  }

  function changePlantCustomer(plantId, customerId, selected) {
    setForm(current => ({
      ...current,
      plantCustomerAssignments: selected
        ? [...current.plantCustomerAssignments, {
          plantId: Number(plantId),
          customerId: Number(customerId),
        }]
        : current.plantCustomerAssignments.filter(assignment =>
          Number(assignment.plantId) !== Number(plantId)
          || Number(assignment.customerId) !== Number(customerId)),
    }));
  }

  function changeGlobalCustomer(customerId, selected) {
    setForm(current => ({
      ...current,
      customerIds: selected
        ? [...new Set([...current.customerIds, Number(customerId)])]
        : current.customerIds.filter(id => Number(id) !== Number(customerId)),
    }));
  }

  function summaryPayload(value) {
    return {
      allPlants: value.allPlants,
      allCustomers: value.allCustomers,
      plantIds: value.allPlants ? [] : [...value.plantIds].map(Number).sort((a, b) => a - b),
      customerIds: value.allPlants && !value.allCustomers
        ? [...value.customerIds].map(Number).sort((a, b) => a - b)
        : [],
      plantCustomerAssignments: !value.allPlants && !value.allCustomers
        ? [...value.plantCustomerAssignments]
          .map(assignment => ({
            plantId: Number(assignment.plantId),
            customerId: Number(assignment.customerId),
          }))
          .sort((a, b) => a.plantId - b.plantId || a.customerId - b.customerId)
        : [],
    };
  }

  async function save(event) {
    event.preventDefault();
    setError("");
    setSuccess("");
    const payload = summaryPayload(form);
    if (original?.configured
      && JSON.stringify(payload) !== JSON.stringify({
        allPlants: original.allPlants,
        allCustomers: original.allCustomers,
        plantIds: original.plantIds,
        customerIds: original.customerIds,
        plantCustomerAssignments: original.plantCustomerAssignments,
      })
      && !window.confirm("This replaces the saved Viewer access and removes any assignments outside the new scope. Continue?")) {
      return;
    }

    setSaving(true);
    try {
      await api.put(`/admin/users/${user.userId}/viewer-access`, payload);
      setSuccess("Viewer access saved.");
      setOriginal({ ...payload, configured: true });
      await onSaved();
    } catch (exception) {
      setError(exception.message || "Unable to save Viewer access.");
    } finally {
      setSaving(false);
    }
  }

  const selectionNote = original?.configured
    && !original.allPlants
    && JSON.stringify([...form.plantIds].sort((a, b) => a - b))
      !== JSON.stringify(original.plantIds)
    ? "Changing plant access removes customer assignments for plants no longer selected."
    : "";
  const selectedCustomersForPlant = plantId => {
    const mappedIds = new Set(plantCustomers
      .filter(mapping => Number(mapping.plantId) === Number(plantId))
      .map(mapping => Number(mapping.customerId)));
    return customers.filter(customer =>
      customer.isActive && mappedIds.has(Number(customer.customerId)));
  };
  const plantSummary = form.plantIds.map(plantId => {
    const plant = plants.find(item => Number(item.plantId) === Number(plantId));
    const assignedNames = form.plantCustomerAssignments
      .filter(assignment => Number(assignment.plantId) === Number(plantId))
      .map(assignment => customers.find(customer =>
        Number(customer.customerId) === Number(assignment.customerId))?.customerName)
      .filter(Boolean);
    return `${plant?.plantName || "Plant"}: ${assignedNames.join(", ") || "No customers selected"}`;
  });
  const selectedPlantNames = form.plantIds
    .map(id => plants.find(plant => Number(plant.plantId) === Number(id))?.plantName)
    .filter(Boolean);
  const selectedGlobalCustomers = availableCustomers
    .filter(customer => form.customerIds.includes(Number(customer.customerId)))
    .map(customer => customer.customerName);

  return <div className="viewer-access-backdrop" role="presentation" onMouseDown={event => {
    if (event.target === event.currentTarget && !saving) onClose();
  }}>
    <section className="viewer-access-dialog" role="dialog" aria-modal="true" aria-labelledby="viewer-access-title">
      <header>
        <div><span>VIEWER ACCESS CONFIGURATION</span><h2 id="viewer-access-title">{user.fullName}</h2><p>{user.email || "No email address"} · Home plant: {user.plantName}</p></div>
        <button type="button" className="viewer-access-close" onClick={onClose} aria-label="Close" disabled={saving}><X size={18}/></button>
      </header>
      {error && <div className="viewer-access-error" role="alert">{error}</div>}
      {success && <div className="viewer-access-success" role="status">{success}</div>}
      {loading ? <div className="viewer-access-state">Loading saved access and available options…</div>
        : error && !original ? <div className="viewer-access-state">Access settings could not be loaded. No changes were made.</div>
        : <form onSubmit={save}>
          {!original?.configured && <div className="viewer-access-warning">No access is assigned yet. This Viewer cannot access report data until you save an explicit scope.</div>}
          <fieldset>
            <legend>Plant access</legend>
            <label className="viewer-access-toggle">
              <input type="checkbox" checked={form.allPlants} disabled={!superAdmin && !form.allPlants}
                onChange={event => setForm(current => ({
                  ...current,
                  allPlants: event.target.checked,
                  plantIds: [],
                  customerIds: event.target.checked
                    ? [...new Set(current.plantCustomerAssignments.map(assignment =>
                      Number(assignment.customerId)))]
                    : [],
                  plantCustomerAssignments: [],
                }))}/>
              <span><strong>All Plants</strong><small>Include future plants automatically. Only Super Admin can grant global plant access.</small></span>
            </label>
            {!form.allPlants && <label className="viewer-access-field">Specific Plants
              <div className="viewer-access-check-list">
                {activePlants.map(plant => {
                  const selected = form.plantIds.includes(Number(plant.plantId));
                  return <label key={plant.plantId} className={`viewer-access-check-item${selected ? " selected" : ""}`}>
                    <input type="checkbox" checked={selected}
                      onChange={event => changePlants(plant.plantId, event.target.checked)}/>
                    <span>{plant.plantName}</span>
                  </label>;
                })}
                {!activePlants.length && <small>No active plants are available.</small>}
              </div>
              <small>Choose every plant this Viewer should access.</small>
            </label>}
          </fieldset>
          <fieldset>
            <legend>Customer access</legend>
            <label className="viewer-access-toggle">
              <input type="checkbox" checked={form.allCustomers}
                onChange={event => setForm(current => ({
                  ...current,
                  allCustomers: event.target.checked,
                  customerIds: [],
                }))}/>
              <span><strong>All Customers</strong><small>Include all active customers mapped to the authorized plants, including future mappings.</small></span>
            </label>
            {!form.allCustomers && (form.allPlants
              ? <div className="viewer-access-field">Specific Customers across all plants
                <div className="viewer-access-check-list">
                  {availableCustomers.map(customer => {
                    const selected = form.customerIds.includes(Number(customer.customerId));
                    return <label key={customer.customerId} className={`viewer-access-check-item${selected ? " selected" : ""}`}>
                      <input type="checkbox" checked={selected}
                        onChange={event => changeGlobalCustomer(customer.customerId, event.target.checked)}/>
                      <span>{customer.customerName}</span>
                    </label>;
                  })}
                  {!availableCustomers.length && <small>No active customers are mapped to active plants.</small>}
                </div>
                <small>These selected customers are available at every active or future plant where they are mapped.</small>
              </div>
              : <div className="viewer-access-field">Specific Customers by plant
                {!form.plantIds.length && <small>Select one or more plants first.</small>}
                {form.plantIds.map(plantId => {
                  const plant = plants.find(item => Number(item.plantId) === Number(plantId));
                  const options = selectedCustomersForPlant(plantId);
                  return <fieldset className="viewer-access-plant-customers" key={plantId}>
                    <legend>{plant?.plantName || `Plant ${plantId}`}</legend>
                    <div className="viewer-access-check-list">
                      {options.map(customer => {
                        const selected = form.plantCustomerAssignments.some(assignment =>
                          Number(assignment.plantId) === Number(plantId)
                          && Number(assignment.customerId) === Number(customer.customerId));
                        return <label key={customer.customerId} className={`viewer-access-check-item${selected ? " selected" : ""}`}>
                          <input type="checkbox" checked={selected}
                            onChange={event => changePlantCustomer(
                              plantId,
                              customer.customerId,
                              event.target.checked)}/>
                          <span>{customer.customerName}</span>
                        </label>;
                      })}
                      {!options.length && <small>No active customers are mapped to this plant.</small>}
                    </div>
                  </fieldset>;
                })}
                <small>Select customers separately for each plant. Only valid, active plant/customer mappings are shown.</small>
              </div>)}
          </fieldset>
          {selectionNote && <div className="viewer-access-note">{selectionNote}</div>}
          <div className="viewer-access-summary"><strong>Effective scope preview</strong><span>
            {form.allPlants ? "All active and future plants" : form.plantIds.length
              ? selectedPlantNames.join(", ")
              : "No plants selected"}
            {" · "}
            {form.allCustomers
              ? "All customers mapped to those plants"
              : form.allPlants
                ? selectedGlobalCustomers.join(", ") || "No customers selected"
                : plantSummary.join(" · ") || "No customers selected"}
          </span></div>
          <footer><button type="button" className="btn-secondary" onClick={onClose} disabled={saving}>Cancel</button><button type="submit" className="btn-primary" disabled={saving||!form.allPlants&&!form.plantIds.length||!form.allCustomers&&(form.allPlants?!form.customerIds.length:!form.plantCustomerAssignments.length)}><Save size={15}/>{saving?"Saving…":"Save Viewer access"}</button></footer>
        </form>}
    </section>
  </div>;
}
