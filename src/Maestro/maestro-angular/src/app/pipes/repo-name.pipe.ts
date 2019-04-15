import { Pipe, PipeTransform } from '@angular/core';

@Pipe({
  name: 'repoName'
})
export class RepoNamePipe implements PipeTransform {

  transform(repo: string): string | undefined {
    if (!repo) {
      return;
    }
    if (!repo.includes("github.com")) {
      return repo.split("/").slice(-1).join("/");
    }
    return repo.split("/").slice(-2).join("/");
  }

}
