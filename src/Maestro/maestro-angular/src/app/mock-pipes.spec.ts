import { Pipe, PipeTransform } from "@angular/core";



@Pipe({name: "commitLink"})
export class MockCommitLink implements PipeTransform {
  transform(value: any): any {
    return `commitLink(${value})`;
  }
}

@Pipe({name: "buildLink"})
export class MockBuildLink implements PipeTransform {
  transform(value: any): any {
    return `buildLink(${value})`;
  }
}

@Pipe({name: "getRepositoryName"})
export class MockGetRepositoryName implements PipeTransform {
  transform(value: any): any {
    return `getRepositoryName(${value})`;
  }
}

@Pipe({name: "repoName"})
export class MockRepoName implements PipeTransform {
  transform(value: any): any {
    return `repoName(${value})`;
  }
}
