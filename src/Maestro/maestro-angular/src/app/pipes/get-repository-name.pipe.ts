import { Pipe, PipeTransform } from '@angular/core';
import { Build } from 'src/maestro-client/models';

@Pipe({
  name: 'getRepositoryName'
})
export class GetRepositoryNamePipe implements PipeTransform {
  transform(build: Build): string | undefined {
    return build.gitHubRepository || build.azureDevOpsRepository;
  }
}
