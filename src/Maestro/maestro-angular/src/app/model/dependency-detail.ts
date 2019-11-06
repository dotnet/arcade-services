export class DependencyDetail {
  name?: string;
  version?: string;
  repoUrl?: string;
  commit?: string;
  pinned?: boolean;
  type?: string;
  coherentParentDependencyName?: string;
  locations?: string[];
  fromToolset?: boolean;
}
