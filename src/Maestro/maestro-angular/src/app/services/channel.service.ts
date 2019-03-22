import { Injectable } from "@angular/core";
import { Observable } from "rxjs";
import { Channel, DefaultChannel } from 'src/maestro-client/models';
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

@Injectable({
  providedIn: "root",
})
export class ChannelService {
  private channels$: Observable<Channel[]>;

  public constructor(private maestro: MaestroService) {
    this.channels$ = maestro.channels.listChannelsAsync().pipe(
      shareReplay(1),
      map(channels => channels.filter(c => c.classification !== "test").sort(channelSorter)),
    );
  }

  public getChannels(): Observable<Channel[]> {
    return this.channels$;
  }

  public getRepositories(channelId: number): Observable<DefaultChannel[]> {
    return this.maestro.defaultChannels.listAsync(undefined, channelId);
  }
}
