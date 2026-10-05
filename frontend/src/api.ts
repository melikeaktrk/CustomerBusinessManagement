import axios from "axios";
export const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL ?? "https://localhost:7001/api",
});
api.interceptors.request.use((config) => {
  const t = sessionStorage.getItem("token");
  if (t) config.headers.Authorization = `Bearer ${t}`;
  return config;
});
api.interceptors.response.use(
  (r) => r,
  (e) => {
    if (e.response?.status === 401) {
      sessionStorage.removeItem("token");
      if (location.pathname !== "/login") location.assign("/login");
    }
    // RFC 7807 yanıtlarını ve eski message alanlarını arayüzde tek mesaj biçimine dönüştür.
    const body = e.response?.data;
    const apiMessage = body?.detail ?? body?.message ?? body?.title;
    if (typeof apiMessage === "string" && apiMessage.trim())
      e.message = apiMessage;
    return Promise.reject(e);
  },
);
