// Result format of Azure DevOps api
// https://docs.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-5.0

export interface BuildListResultCompleted {
  count: number;
  value: BuildStatusCompleted[];
}

export interface BuildStatusCompleted {
  id: number;
  finishTime: string;
  status: "completed";
  result: "canceled" | "failed" | "none" | "partiallySucceeded" | "succeeded";
  length: number;
  _links: {
    web: {
      href: string;
    }
  }
}

export interface BuildListResultInProgress{
  count: number;
  value: BuildStatusInProgress[];
}

export interface BuildStatusInProgress {
  id: number;
  status: "inProgress";
  _links: {
    web: {
      href: string;
    }
  }
}