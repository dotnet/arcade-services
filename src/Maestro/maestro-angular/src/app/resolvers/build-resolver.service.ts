import { Injectable } from '@angular/core';
import { Build } from 'src/maestro-client/models';
import { Resolve, ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { Observable, of } from 'rxjs';
import { BuildService } from '../services/build.service';
import { switchMap } from 'rxjs/operators';
import { StatefulResult } from 'src/stateful';

@Injectable({
  providedIn: 'root'
})
export class BuildResolverService implements Resolve<StatefulResult<Build> | "latest" | undefined> {

  constructor(private buildService: BuildService) { }

  resolve(route: ActivatedRouteSnapshot): Observable<StatefulResult<Build> | "latest" | undefined> {
    const buildId = route.paramMap.get("buildId");
    const channelId = route.paramMap.get("channelId");
    const repository = route.paramMap.get("repository");

    if(buildId && channelId && repository) {
      if (buildId === "latest") {
        return of("latest");
      }

      return of(buildId).pipe(
        switchMap(id => this.buildService.getBuild(+id)),
      );
    }

    return of(undefined);
  }
}
