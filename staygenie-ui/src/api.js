const API_BASE = import.meta.env.VITE_API_URL ?? "https://localhost:7115";

// Har API call yahin se: token header khud lagta hai, error ka saaf message milta hai
export async function api(path, { token, method = "GET", body } = {}) {
  const headers = {};
  if (body !== undefined) headers["Content-Type"] = "application/json";
  if (token) headers.Authorization = `Bearer ${token}`;

  const res = await fetch(`${API_BASE}${path}`, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });

  if (res.status === 204) return null;

  let data = null;
  const text = await res.text();
  if (text) {
    try {
      data = JSON.parse(text);
    } catch {
      data = text;
    }
  }

  if (!res.ok) {
    let message = "Something went wrong. Please try again.";
    if (res.status === 403) message = "You do not have access to this.";
    else if (res.status === 429) message = "Too many requests. Please wait a minute.";
    else if (typeof data === "string" && data) message = data;
    else if (data?.message || data?.title) message = data.message || data.title;

    const err = new Error(message);
    err.status = res.status;
    throw err;
  }

  return data;
}