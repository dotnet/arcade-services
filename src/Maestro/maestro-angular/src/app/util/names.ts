export function prettyRepository(repo: string): string {
  if (!repo.includes("github.com")) {
    return repo.split("/").slice(-1).join("/");
  }
  return repo.split("/").slice(-2).join("/");
}
