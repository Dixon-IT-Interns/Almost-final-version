const API_BASE_URL = import.meta.env.VITE_API_URL || "http://localhost:5211/api";

async function request(path, options = {}, responseType = "json") {
  const headers = new Headers(options.headers);
  const token = localStorage.getItem("dcc-token");

  if (options.body && !(options.body instanceof FormData)) {
    headers.set("Content-Type", "application/json");
  }

  if (token) {
    headers.set("Authorization", `Bearer ${token}`);
  }

  let response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, { ...options, headers });
  } catch (error) {
    if (error?.name === "AbortError") throw error;
    throw new Error("Could not reach the API. Check the backend URL and that the API is running.", { cause: error });
  }

  if (!response.ok) {
    const error = await response.json().catch(() => null);
    if (response.status === 404) {
      throw new Error(error?.message || `API endpoint not found (${path}). Restart the backend from Command-Center-Backend so it loads the current controllers.`);
    }
    if (response.status === 401) {
      localStorage.removeItem("dcc-session");
      localStorage.removeItem("dcc-token");
      window.dispatchEvent(new Event("dcc-session-expired"));
    }
    throw new Error(error?.message || `Request failed (${response.status}).`);
  }

  if (response.status === 204) return null;
  return responseType === "blob" ? response.blob() : response.json();
}

export const api = {
  get: (path, options = {}) => request(path, options),
  download: (path) => request(path, {}, "blob"),
  post: (path, body) => request(path, { method: "POST", body: JSON.stringify(body) }),
  postFormData: (path, body) => request(path, { method: "POST", body }),
  put: (path, body) => request(path, { method: "PUT", body: JSON.stringify(body) }),
  patch: (path, body) => request(path, { method: "PATCH", body: JSON.stringify(body) }),
  delete: (path) => request(path, { method: "DELETE" }),
};
