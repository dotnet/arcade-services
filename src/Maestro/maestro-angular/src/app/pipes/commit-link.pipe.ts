import { Pipe, PipeTransform } from '@angular/core';
import { Build } from 'src/maestro-client/models';

@Pipe({
  name: 'commitLink'
})
export class CommitLinkPipe implements PipeTransform {

  transform(value: Build): string | undefined {
    if (!value) {
      return;
    }

    if (value.gitHubRepository) {
      return `${value.gitHubRepository}/commits/${value.commit}`;
    }

    if (value.azureDevOpsRepository) {
      return `${value.azureDevOpsRepository}?_a=history&version=GC${value.commit}`;
    }
  }

}
