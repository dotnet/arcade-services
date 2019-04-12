import { Pipe, PipeTransform } from '@angular/core';
import { Build } from 'src/maestro-client/models';

@Pipe({
  name: 'buildLink'
})
export class BuildLinkPipe implements PipeTransform {

  transform(value: Build): string | undefined {
    if (!value ||
        !value.azureDevOpsAccount ||
        !value.azureDevOpsProject) {
      return;
    }
    return `https://dev.azure.com` +
      `/${value.azureDevOpsAccount}` +
      `/${value.azureDevOpsProject}` +
      `/_build/results` +
      `?view=results&buildId=${value.azureDevOpsBuildId}`;
  }

}
