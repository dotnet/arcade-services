import { Component, Input, OnInit, OnDestroy } from "@angular/core";
import { NavigationEnd, Router } from "@angular/router";
import { Observable, SubscriptionLike, of } from "rxjs";
import { filter, map, switchMap, concat, shareReplay, tap } from "rxjs/operators";

import { Channel, DefaultChannel } from 'src/maestro-client/models';
import { ChannelService } from 'src/app/services/channel.service';
import { statefulSwitchMap, StatefulResult, statefulPipe } from 'src/stateful';
import { transition, trigger, animate, style } from '@angular/animations';

@Component({
  selector: "mc-side-bar-channel",
  templateUrl: "./side-bar-channel.component.html",
  styleUrls: ["./side-bar-channel.component.scss"],
  animations: [
    trigger("expandCollapse", [
      transition("void => loading", [
        style({ height: 0 }),
        animate("400ms ease-out", style({ height: "*" })),
      ]),
      transition("void => loaded", [
        style({ height: 0 }),
        animate("400ms ease-out", style({ height: "*" })),
      ]),
      transition("loading => loaded", [
        style({ height: "30px" }),
        animate("400ms ease-out", style({ height: "*" })),
      ]),
      transition("* => void", [
        style({ height: "*" }),
        animate("400ms ease-in", style({ height: 0 })),
      ]),
    ]),
  ]
})
export class SideBarChannelComponent implements OnInit, OnDestroy {

  public constructor(private channelService: ChannelService, private router: Router) { }

  @Input() public channel!: Channel;
  @Input() public index!: number;

  public isCollapsed = true;

  public branches$!: Observable<StatefulResult<DefaultChannel[]>>;

  private routeSubscription?: SubscriptionLike;

  public state?: string;

  public ngOnInit() {
    this.branches$ = of(this.channel.id).pipe(
      statefulSwitchMap(channelId => {
        return this.channelService.getRepositories(channelId);
      }),
      shareReplay(1),
    );

    // Get the current route state, and concat it with any changes
    this.routeSubscription =
      of(this.router.routerState.root).pipe(
        concat(this.router.events.pipe(
          filter(evt => evt instanceof NavigationEnd),
          map(() => this.router.routerState.root),
        )),
        map(route => {
          while (route.firstChild) route = route.firstChild;
          return route;
        }),
        switchMap(r => r.paramMap),
      ).subscribe(params => {
        const channelIdStr = params.get("channelId")
        if (channelIdStr) {
          const channelId = +channelIdStr;
          this.isCollapsed = this.channel.id !== +channelId;
        }
      });
  }

  public ngOnDestroy() {
    if (this.routeSubscription) {
      this.routeSubscription.unsubscribe();
    }
  }
}
