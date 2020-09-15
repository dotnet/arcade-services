// Result format of Azure DevOps api
// https://docs.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-5.0
export type BuildStatus = "all" | "cancelling" | "completed" | "inProgress" | "none" | "notStarted" | "postponed";

export interface Build {
  _links: { web: { href: string; } };
  id: number;
  status: BuildStatus;
}

export interface CompletedBuild extends Build {
  status: "completed";
  finishTime: string;
  length: number;
  result: "canceled" | "failed" | "none" | "partiallySucceeded" | "succeeded";
}

export interface BuildListResult<T extends Build> {
  count: number;
  value: T[];
}