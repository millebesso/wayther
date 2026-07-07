---
title: Wayther PRD
description: High-level PRD for an application that produces weather forecasts for a route.
---

## Introduction

Wayther is an application that creates a route between two places and produces a weather forecast for the route.

The following is a scenario where Wayther is useful. You are going to drive between locations A and B and it's expected to take 4 hours. There are changing weather conditions during the day so you want to make sure that you take your coffee break at a time and locatin when it's not raining. Wayther will provide the route between A and B and the weather at the places where you are expected to be at some interval (every 30 minutes, every 60 minutes or so). Additionally it can also provide the weather at locations you pass at the tima when you are expected to be there.

## Tech Stack

Wayther should be built with a dotnet backend and a React frontend. The RDBMs should be postgres. It should be fully containerized, including the data storage. It should be built with a target of 75% test coverage for backend code.
