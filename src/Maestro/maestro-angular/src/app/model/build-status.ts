// Result format of Azure DevOps api
// https://docs.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-5.0

export interface BuildListResult {
  count: number;
  value: BuildStatus[];
}

export interface BuildStatus {
  id: number;
  finishTime: string;
  status: "all" | "cancelling" | "completed" | "inProgress" | "none" | "notStarted" | "postponed";
  result: "canceled" | "failed" | "none" | "partiallySucceeded" | "succeeded";
  length: number;
}
