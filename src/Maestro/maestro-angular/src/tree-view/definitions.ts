import { Observable } from "rxjs";

export interface StaticTreeNode {
  data: any;
  startOpen?: boolean;
  children?: TreeNode[];
}

export interface DynamicTreeNode {
  data: any;
  startOpen?: boolean;
  children$: () => Observable<TreeNode[]>;
}

export type TreeNode = StaticTreeNode | DynamicTreeNode;
