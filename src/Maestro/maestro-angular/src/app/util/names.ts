export function prettyRepository(repo: string): string {
  return repo.split("/").slice(-2).join("/");
}
