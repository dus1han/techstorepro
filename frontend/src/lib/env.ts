/**
 * Reads and validates the public runtime configuration once, at module load, so a
 * missing variable fails loudly at startup instead of as a confusing fetch error later.
 */
function required(name: string, value: string | undefined): string {
  if (!value) {
    throw new Error(`Missing environment variable: ${name}. See .env.example.`);
  }
  return value;
}

export const env = {
  apiBaseUrl: required(
    "NEXT_PUBLIC_API_BASE_URL",
    process.env.NEXT_PUBLIC_API_BASE_URL,
  ),
} as const;
