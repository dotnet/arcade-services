import { Asset } from "src/app/model/asset";

export interface Build {
  id: number;
  buildNumber: string;
  repository: string;
  branch: string;
  channelIds: number[];
  commit: string;
  dateProduced: Date;
  link: string;
  dependencies: number[];
  assets: Asset[];
  azureDevOpsInfo: {
    account: string;
    project: string;
    repository: string;
    definitionId: number;
    buildId: number;
  };
}
