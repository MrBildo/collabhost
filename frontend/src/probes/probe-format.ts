/** Convert a camelCase string to Title Case for display.
 * "moduleSystem" -> "Module System", "isAspNetCore" -> "Is Asp Net Core"
 */
function camelToTitle(key: string): string {
  const spaced = key.replace(/([A-Z])/g, ' $1').trim()
  return spaced.charAt(0).toUpperCase() + spaced.slice(1)
}

export { camelToTitle }
