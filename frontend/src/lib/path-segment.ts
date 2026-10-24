/**
 * Encode one value for use as a single segment of an API path.
 *
 * Route params such as an app slug come from the address bar, and React Router
 * decodes `%2F` back to `/` inside a param. Pasted into an API path raw, a
 * crafted link like `/apps/..%2Fusers` would steer the dashboard's request (sent
 * with the operator's key) to a different endpoint. Percent-encoding keeps the
 * value inside its one segment.
 *
 * `encodeURIComponent` leaves `.` alone, and the URL parser resolves a lone `.`
 * or `..` segment as a directory step, so those two values are refused outright,
 * as is the empty string (which would collapse the path onto its parent). No
 * real slug or id is ever empty or a bare dot segment.
 */
function encodePathSegment(value: string): string {
  if (value === '' || value === '.' || value === '..') {
    throw new Error(`"${value}" is not a valid path segment`)
  }
  return encodeURIComponent(value)
}

export { encodePathSegment }
