import { Injectable } from "@angular/core";
import { Observable, combineLatest } from "rxjs";
import { Channel, DefaultChannel, Subscription } from 'src/maestro-client/models';
import { MaestroService } from 'src/maestro-client';
import { shareReplay, map } from 'rxjs/operators';

function channelSorter(a: Channel, b: Channel): number {
  if (a.name == b.name) {
    return 0;
  }
  if (a.name! < b.name!) {
    return 1;
  }
  return -1;
}

function repoSorter(a: DefaultChannel, b: DefaultChannel): number {
  if (a.repository == b.repository) {
    return 0;
  }
  if (a.repository! < b.repository!) {
    return -1;
  }
  return 1;
}

@Injectable({
  providedIn: "root",
})
export class ChannelService {
  private channels$: Observable<Channel[]>;

  public constructor(private maestro: MaestroService) {
    this.channels$ = maestro.channels.listChannelsAsync({}).pipe(
      shareReplay(1),
      map(channels => channels.filter(c => c.classification !== "test").sort(channelSorter)),
    );
  }

  public getChannels(): Observable<Channel[]> {
    return this.channels$;
  }

  public getRepositories(channelId: number): Observable<DefaultChannel[]> {
    return this.buildRepositoriesList(channelId);
  }

  private buildRepositoriesList(channelId: number): Observable<DefaultChannel[]> {
    let builds = this.maestro.builds.listBuildsAsync({ channelId: channelId });

    let targetRepos = builds.pipe(map(x => x.map(y => {
      return new DefaultChannel({
        repository: y.gitHubRepository === undefined ? y.azureDevOpsRepository! : y.gitHubRepository!,
        branch: y.gitHubBranch === undefined ? y.azureDevOpsBranch : y.gitHubBranch,
        id: 0,
      });
    })));

    const repos = targetRepos.pipe(
      map(l => {
        let dcArray = new Array<DefaultChannel>().concat(l);
        dcArray = dcArray.filter((dc, index) => dc.repository != undefined);
        return dcArray.filter((dc, index) => dcArray.findIndex(t => t.repository === dc.repository) === index).sort(repoSorter);
      }),
    );
    return repos; 
  }
}
