import axios from 'axios';

const ADMIN_KEY_STORAGE = 'collabhost-admin-key';

export function getAdminKey(): string | null {
  return localStorage.getItem(ADMIN_KEY_STORAGE);
}

export function setAdminKey(key: string) {
  localStorage.setItem(ADMIN_KEY_STORAGE, key);
}

export function clearAdminKey() {
  localStorage.removeItem(ADMIN_KEY_STORAGE);
}

export const api = axios.create({
  baseURL: '/api/v1',
});

api.interceptors.request.use((config) => {
  const key = getAdminKey();
  if (key) {
    config.headers['X-User-Key'] = key;
  }
  return config;
});
