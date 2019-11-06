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
// new
      let builds = this.maestro.channels.listRepositoriesAsync({ id: channelId });

      let repos = builds.pipe(
          map(x => x.map(y => {
            return new DefaultChannel({
            repository: y ,
            branch: undefined,
            id: 0,
          });
        })));
      return repos;
  }
}
