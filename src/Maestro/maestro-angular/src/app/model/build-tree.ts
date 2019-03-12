import { Build } from "src/app/model/build";

export interface BuildTree {
  build: Build;
  depth: number;
  children: BuildTree[];
}
